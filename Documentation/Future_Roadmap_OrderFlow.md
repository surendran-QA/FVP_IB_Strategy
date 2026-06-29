# Quantower Strategy Roadmap: Future Order Flow & Microstructure Systems

> **Purpose of this Document:** This roadmap details the conceptual design, architectural blueprints, and quantitative mechanics for future trading strategy development in Quantower, specifically moving beyond time-bounded Volume Profile models into high-velocity **Tick-Bar and Order Flow Microstructure** systems.

---

## 1. Architectural Foundation: 1-Minute Time Bars vs. Tick Bars

Before designing future strategies, it is essential to understand the correct tool for the job.

### When to Use 1-Minute Time Bars (`Period.MIN1`)
* **Core Use Case:** Mandatory for time-bounded strategies like `FVP_IB_Strategy` (Initial Balance 19:00–19:30 IST).
* **Wall-Clock Cutoff Precision:** Guarantees that the 19:30 IST execution window aligns to the exact second without delay.
* **Profile Cleanliness:** Prevents post-open active volume from bleeding into the pre-open Initial Balance calculation.
* **Backtesting Efficiency:** Highly predictable memory usage and execution speed in Quantower's backtester (~100k bars/year).

### When to Use Tick Bars (e.g., 500-Tick or 1000-Tick)
* **Core Use Case:** Highly recommended for continuous, non-time-bounded **Order Flow & Microstructure Scalping Strategies**.
* **The Timing Behavior:** A tick bar closes based on transaction count (e.g., 1000 trades), not the clock. A tick bar may open at `19:29:48` and close at `19:30:14`. While inappropriate for strict clock cutoffs, it is the ultimate gauge for measuring market momentum, aggressive participation, and volatility expansion.

---

## 2. Future Strategy Blueprint: The Tick-Imbalance Scalper (TIS)

### Concept & Objectives
* **Trading Style:** High-velocity intraday momentum scalping.
* **Target Instruments:** US Equity Index Futures (`MNQ` / `NQ` / `ES`).
* **Core Philosophy:** Rather than waiting for a macro 30-minute window, the strategy operates on rapid tick charts (e.g., 500-Tick or 1000-Tick) to capture rapid intraday momentum bursts driven by institutional order flow imbalances.

```
┌─────────────────────────────────────────────────────────┐
│              Tick-Imbalance Scalper (TIS)               │
├────────────────────────────┬────────────────────────────┤
│     Primary Indicators     │     Execution Mechanics    │
├────────────────────────────┼────────────────────────────┤
│ 1. Order Book Delta        │ • Instant Momentum Entry   │
│ 2. Bar Velocity (Pace)     │ • Dynamic Tick Stop Loss   │
│ 3. Cumulative Delta        │ • Adaptive Trailing Bracket│
└────────────────────────────┴────────────────────────────┘
```

---

## 3. Key Quantitative Indicators & Mechanics

### A. Order Book Imbalance & Delta Surge
* **The Mechanic:** Track the aggressive Bid vs. Ask volume delta within each tick bar. 
* **The Trigger:** A sudden, statistically significant surge in positive delta (e.g., +80% of volume transacted at the Ask in a 1000-tick bar) indicates aggressive institutional market buying, triggering an immediate momentum long.

### B. Pace of Tape (Bar Velocity)
* **The Mechanic:** Measure the exact time (in milliseconds) it takes for a 1000-tick bar to complete.
* **The Trigger:** During quiet periods, a 1000-tick bar might take 30 seconds to complete. If the average bar completion time suddenly compresses from 30 seconds down to 2 seconds, it instantly signals a massive institutional liquidity event and volatility expansion.

### C. Adaptive Trailing Brackets
* **The Mechanic:** Because tick charts naturally adapt to market speed (generating more bars during fast moves and fewer bars during slow moves), trailing stop losses update per tick bar completion rather than waiting for rigid clock intervals.
* **The Advantage:** Locks in profits instantly during high-volatility spikes before mean reversion occurs.

---

## 4. Development Milestones & Checklist
- `[ ]` Scaffold `TickImbalanceScalper` project in Quantower C# SDK.
- `[ ]` Implement custom `Core.Instance.VolumeAnalysis` delta parsing per tick bar.
- `[ ]` Implement millisecond timer to track Bar Velocity (`bar.TimeRight - bar.TimeLeft`).
- `[ ]` Build dynamic trailing bracket logic that updates on tick bar close.
- `[ ]` Validate backtest performance and memory consumption using 500-tick and 1000-tick historical data streams.

---

## 5. Brainstorm Idea: Modular Multi-DLL Indicator Fusion (VP + Order Flow)

### The Architectural Vision: Decoupled Modularity
Rather than creating a single monolithic DLL that bundles both Volume Profile and Order Flow calculations into one massive, difficult-to-maintain basket, the objective is to build **two separate, highly specialized indicator DLLs** that communicate seamlessly to generate merged signals.

```
┌─────────────────────────────────┐       ┌─────────────────────────────────┐
│     FVP_IB_Visualizer.dll       │       │    FVP_OrderFlow_Imbalance.dll  │
│    (Master VP Engine - MIN1)    │       │    (Real-Time Tape Engine - TICK)│
└────────────────┬────────────────┘       └────────────────┬────────────────┘
                 │   Publishes 19:30 Levels                │ Subscribes & Monitors Tape
                 │   (IB_POC, VAH, VAL)                    │ at VP Level (Pace & Delta)
                 ▼                                         ▼
        ┌───────────────────────────────────────────────────────────┐
        │            FVP_Common_DataBus.dll (Memory Bus)            │
        └─────────────────────────────┬─────────────────────────────┘
                                      │
                                      ▼
                        [ FVP Master Strategy DLL ]
                        (Fuses Signals & Executes)
```

### How Two Separate Quantower DLLs Communicate (3 Enterprise Patterns)

#### 1. The Shared Core Memory Bus (`FVP_Common_DataBus.dll`)
* **How it works:** Since all Quantower indicators and strategies run inside the same master C# process memory space (`AppDomain`), we create a lightweight third library called `FVP_Common_DataBus.dll`.
* **The Workflow:**
  1. **VP Indicator (`FVP_IB_Visualizer.dll`):** Calculates `IB_POC`, `IB_VAH`, and `IB_VAL` at 19:30 IST and pushes them to the in-memory registry: `DataBus.Instance.PublishProfile("MNQU26", marketData);`.
  2. **Order Flow Indicator (`FVP_OrderFlow_Imbalance.dll`):** Subscribes to the bus: `var activeVP = DataBus.Instance.GetLatestProfile("MNQU26");`. 
  3. **Signal Generation:** When the live price approaches `activeVP.IB_VAL`, the Order Flow indicator activates its sub-second tape reader (looking for Delta Imbalance or Pace of Tape acceleration) and paints the final confirmed **Combined Signal** directly on the chart.

#### 2. Quantower Native Indicator Injection (`GetIndicator`)
* **How it works:** Quantower's SDK allows any custom Indicator or Strategy to directly instantiate and query another compiled Indicator using the Core API.
* **The Workflow:**
  ```csharp
  // Inside the Order Flow Indicator's OnInit():
  var vpIndicator = Core.Instance.Indicators.GetIndicator("FVP IB Visualizer", new[] { CurrentSymbol, CurrentAccount });
  this.hdm.AddIndicator(vpIndicator);
  
  // Inside OnUpdate():
  double currentPoc = vpIndicator.GetValue(0); // Directly reading the VP level from the other DLL!
  ```

#### 3. Memory-Mapped Files (MMF) / Named Pipes (Inter-Process)
* If we ever decide to run the Order Flow AI engine in Python or C++ for ultra-low latency machine learning inference, Memory-Mapped Files allow the Quantower C# DLL to share C# structs in RAM with external processes at sub-microsecond speeds.

### Advantages of the Modular Multi-DLL Approach
1. **Separation of Concerns:** Volume Profile code remains completely untouched and pristine. Order Flow logic can be iterated on daily without risking bugs in the core VP engine.
2. **Independent Timeframes:** The VP indicator runs efficiently on 1-Minute time bars (`MIN1`), while the Order Flow indicator operates on lightning-fast Tick Bars (`1000-Tick`).
3. **Pluggable Strategy Architecture:** You can mix and match indicators in future strategies (e.g., swapping the Order Flow indicator for a Volatility indicator while keeping the master VP engine).
