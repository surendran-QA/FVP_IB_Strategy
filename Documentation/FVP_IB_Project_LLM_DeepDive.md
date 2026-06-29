# FVP_IB_Strategy: Comprehensive Master Architectural & Algorithmic Deep-Dive

> **Purpose of this Document:** This report is structured as an all-inclusive onboarding guide and master reference for Large Language Models (specifically Gemini / NotebookLM). It details the complete domain context, system architecture, mathematical calculations, defensive engineering guardrails, and data schemas of the **FVP_IB_Strategy** and its visual companion **IBVisualizerIndicator**.

---

## 1. Project Executive Summary

### Domain & Objectives
The **FVP_IB_Strategy** is a professional-grade, automated quantitative trading strategy and visual indicator suite built for the **Quantower C# Trading Platform**. 
* **Target Instruments:** US Equity Index Futures (specifically `MNQ` / `NQ` — Micro E-mini / E-mini Nasdaq-100).
* **Core Strategy Concept:** The strategy analyzes the market microstructure during the first 30 minutes of the US regular trading hours (19:00 to 19:30 IST / Indian Standard Time). This 30-minute window forms the **Initial Balance (IB)**. 
* **Microstructure Analysis:** By building a Fixed Volume Profile (FVP) exclusively across this 30-minute window, the system classifies the market structure into standard market profile shapes (`bShape`, `PShape`, `BShape`, `DShape`), maps critical high/low volume nodes (`POC`, `VAH`, `VAL`, `HVN2`, `LVN`), and executes highly precise directional breakout or mean-reversion trades.

---

## 2. System Architecture & Component Hierarchy

The project follows a clean modular separation of concerns between state representation, mathematical computation, execution logic, and UI visualization.

```
FVP_IB_Strategy Solution
 │
 ├── Models/
 │    └── MarketData.cs             <-- Clean state transfer object for IB metrics & signals
 │
 ├── Calculations/
 │    ├── InitialBalanceEngine.cs   <-- Historical data parsing & Volume Profile aggregation
 │    ├── ShapeDetector.cs          <-- Profile shape classification & node identification
 │    └── SignalGenerator.cs        <-- Trade setup generation (Entry, TP, SL rules)
 │
 ├── Strategies/
 │    └── FVP_IB_Strategy.cs        <-- Automated execution engine & live order management
 │
 └── UI/Indicators/
      └── IBVisualizerIndicator.cs  <-- Chart UI overlay, label rendering & simulation reporting
```

### A. Data Models (`Models/MarketData.cs`)
Acts as the unified contract passed between engines. 
* **Properties Stored:** `IB_High`, `IB_Low`, `IB_POC`, `IB_VAH`, `IB_VAL`, `IB_HVN1`, `IB_HVN2`, `IB_LVN`, `CurrentShape` (`VolumeProfileShape` enum), calculation timestamps, and the generated `TradeSignal`.

### B. The Computation Engine (`Calculations/InitialBalanceEngine.cs`)
Responsible for ingesting raw historical data and constructing the volume profile.
* **Bar Filtration:** Loops through historical data (`hdm`), converting timestamps from UTC to `India Standard Time`. It captures bars exclusively between `19:00:00` and `19:29:59` IST.
* **Volume Binning:** Bins volume into fixed price step intervals defined by `ProfileStepTicks` (default 4 ticks / 1 point on NQ).
* **Data Source Agnosticism:** If the broker feed supplies precise tick-level volume nodes (`VolumeAnalysisData.PriceLevels`), it aggregates them directly. If not, it gracefully falls back to a linear tick-slicing algorithm (`bar.Volume / ticksInBar`) across the candle's High-Low range.
* **Value Area Calculation:** Identifies the Point of Control (`POC` / highest volume bin). It then expands a search window upwards and downwards from the POC, absorbing the larger volume neighbor at each step until 70% of the total IB volume is enclosed, establishing the Value Area High (`VAH`) and Value Area Low (`VAL`).

### C. The Shape Classification Engine (`Calculations/ShapeDetector.cs`)
Implements an advanced 3-stage classification algorithm to decode market sentiment from the profile distribution:
1. **Primary POC Location Check (Single Distributions):**
   - **`PShape` (Long Liquidation / Short Covering):** `IB_POC` is located above the 60% threshold of the total IB range (`IB_Low + 0.6 * range`).
   - **`bShape` (Long Accumulation / Trend Down):** `IB_POC` is located below the 40% threshold of the total IB range (`IB_Low + 0.4 * range`).
   - **`DShape` (Normal/Range Bound):** `IB_POC` sits between 40% and 60% of the range.
2. **Secondary Peak Detection (`HVN2`):**
   - Scans the profile bins for a secondary local maximum (`HVN2`) that is physically separated from the primary POC (`HVN1`) by at least `DoubleDistMinTicks` (default 40 ticks / 10 points).
3. **Double Distribution Validation (`BShape`):**
   - To prevent market noise from triggering false double distributions, it applies two strict mathematical gates:
     - **Peak Significance Gate:** `HVN2` volume must be at least `HVN2 Min Ratio` (default `0.30` / 30%) of the primary POC volume.
     - **Valley Thinness Gate:** The lowest volume bin (`LVN`) situated between `HVN1` and `HVN2` must dip below `LVN Threshold` (default `0.12` / 12%) of the primary POC volume.
   - If both gates pass, the profile is upgraded to **`BShape`**, and `IB_HVN2` / `IB_LVN` levels are firmly established.

### D. The Trade Signal Engine (`Calculations/SignalGenerator.cs`)
Converts market shapes into strict risk-defined trade structures (`TradeSignal`):
* **`BShape` (Double Distribution Breakout):** 
  - The `LVN` acts as the exact valley of rejection or breakout.
  - **Directional Logic:** If `POC > LVN`, it generates a `SHORT` entry at `LVN`, Stop Loss at `POC`, and Take Profit at `HVN2` (or downward projection). If `POC < LVN`, it generates a `LONG` entry at `LVN`, Stop Loss at `POC`, and Take Profit at `HVN2` (or upward projection).
* **`bShape` / `PShape` / `DShape`:** Configured for standard mean reversion or trend breakout setups across `VAH`, `VAL`, or `POC`.

---

## 3. Automated Strategy Execution & UI Visualization

### The Strategy Engine (`Strategies/FVP_IB_Strategy.cs`)
Executes the fully automated algorithmic logic within Quantower's live or backtesting environment.
* **Timing Gates:** Unlocks calculation at exactly `19:30:00` IST. Operates active trades until the End-of-Day flatten window at `01:30:00` IST.
* **Daily Trade Reset:** Implements `istTime.Date != lastTradedDate` to correctly reset daily trading quotas, bypassing Quantower backtesting quirks where intra-day time windows are occasionally compressed or skipped.
* **Guaranteed EOD Flattening:** Automatically checks for open positions at the `EndTradingTime` (01:30 IST). It explicitly forces an `OrderTypeBehavior.Market` order to close the position, ensuring guaranteed fills regardless of rapid market movements.

### The Indicator UI (`UI/Indicators/IBVisualizerIndicator.cs`)
Renders the visual representation directly onto the user's charts.
* **Pristine Rendering:** Draws `VAH`, `VAL`, `POC`, `High`, and `Low` lines. For `BShape` days, it dynamically adds the `HVN2` (Orange dashed) and `LVN` (Magenta dotted) lines. For single distribution days (`b`, `P`, `D`), secondary lines are hidden to eliminate chart clutter.
* **Label Overlap Prevention:** Features a dynamic Y-coordinate sorting algorithm. If any text label is within 15 pixels vertically of an adjacent label, it shifts the lower label downwards, ensuring 100% readability.
* **Visual Information Table:** Displays an elegant dark-mode overlay box summarizing the active or historical IB shape, preferred side, entry/exit levels, and live trade simulation status.

---

## 4. Standardized CSV Reporting & Comparison Engine

To achieve rigorous scientific validation, both the Strategy and Indicator feature independent CSV reporting engines that output identical 21-column schemas.
* **Strategy Report Path:** `C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\TradeReport.csv` (Records live/backtest actual order executions).
* **Indicator Report Path:** `C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\IndicatorReport.csv` (Records simulated trade setups, including pending, unexecuted, or faded signals).

### Master 21-Column Data Schema
```csv
Day,OrderPlacedTime,EntryFillTime,ExitTime,Symbol,Side,Qty,EntryPrice,ExitPrice,PnL,Status,Result,Shape,IB_High,IB_Low,POC,VAH,VAL,LVN,HVN1,HVN2
```

### Key Analytical Features
* **`Day` Column:** Outputs the text representation of the day of the week (e.g., `Monday`, `Tuesday`). This is engineered specifically for statistical filtering in tools like NotebookLM to identify and skip low win-rate trading days.
* **Comprehensive Level Logging:** Logs all 8 microstructure levels (`IB_High`, `IB_Low`, `POC`, `VAH`, `VAL`, `LVN`, `HVN1`, `HVN2`). For non-BShape days, `LVN` and `HVN2` cleanly output `-`.

---

## 5. Defensive Engineering Guardrails & Quantower API Gotchas

During development, several undocumented behaviors and quirks in the Quantower API (v1.145.x) were discovered and successfully architected around. **Any future code modifications must strictly adhere to these guardrails:**

```markdown
### 1. Order Routing & The Instant Fill Bug
* **The Bug:** Sending a default Market order, or sending a Limit order where the market price has already crossed the limit threshold, causes the exchange to fill the order instantly at market price, ruining backtest entry prices and PnL.
* **The Guardrail:** The strategy dynamically queries `Core.Instance.OrderTypes` for `OrderTypeBehavior.Limit` or `OrderTypeBehavior.Stop` depending on the real-time relationship between `CurrentPrice` and `EntryPrice`.

### 2. Stop Orders & The TriggerPrice Parameter
* **The Bug:** For Stop orders, `PlaceOrderRequestParameters` completely ignores the standard `Price` parameter. If `TriggerPrice` is left unpopulated, the order fails or executes instantly at market price.
* **The Guardrail:** Always populate both fields conditionally:
  `Price = (behavior == Limit) ? entryPrice : default(double),`
  `TriggerPrice = (behavior == Stop) ? entryPrice : default(double)`

### 3. Ghost PositionRemoved Events & Deduplication
* **The Bug:** `Core.PositionRemoved` fires twice for a single trade when bracket orders (SL/TP) are attached — once when the bracket orders cancel, and again when the position officially closes.
* **The Guardrail:** The strategy implements a `HashSet<string>` tracking a composite key of `{obj.OpenTime.Ticks}_{obj.OpenPrice}_{obj.Side}` to bypass duplicates.

### 4. Background Mode: The `BusinessObjectState.Fake` Trap
* **The Bug:** In Background backtesting, the simulator passes a "Fake" Symbol. Replacing this fake symbol with a live one via `Core.Instance.GetSymbol()` causes a fatal mismatch in `PositionRemoved`, permanently deadlocking the strategy because `obj.Symbol == CurrentSymbol` returns false.
* **The Guardrail:** Never override simulation objects in `OnRun()`. Always use robust ID-based matching (`obj.Symbol.Id == this.CurrentSymbol.Id`).

### 5. Background Mode: The Ghost Filter Trap & Physics-Based Reconstruction
* **The Bug:** Backtesting in Background Mode often produces `PositionRemoved` events where `GrossPnL` is NaN and `obj.CurrentPrice` is stale. If a naive "Ghost Filter" is used (`if pnl == 0 && exit == entry, skip trade`), the engine deletes 100% of real, valid trades from the CSV reports.
* **The Guardrail:** Never use a ghost filter based on 0 PnL. Instead, use a Physics-Based reconstruction fallback. The strategy extracts the last tick (`hdm[0].Close`) and compares it against the exact Stop Loss and Take Profit levels (`Math.Abs(currentLast - sl) < Math.Abs(currentLast - tp)`) to perfectly deduce whether the trade won or lost, completely reconstructing the Exit Price and PnL dollar values.

### 6. Timeframe Synchronization (1-Minute Mandatory)
* **The Bug:** Running the indicator or strategy on a 5-minute chart causes Quantower to smear volume equally across large 5-minute candles. This flattens out sharp valleys (`LVN`) and secondary peaks (`HVN2`), destroying `BShape` detection.
* **The Guardrail:** The strategy defaults to `Period.MIN1`. The visual indicator must always be attached to a **1-minute chart** to ensure 100% data alignment between `IndicatorReport.csv` and `TradeReport.csv`.
```

---

## 6. Config Architecture & Verification
* **Centralized Configuration:** Hardcoded absolute paths (e.g., `C:\AMP Quantower\...`) destroy portability and cause conflicts. The strategy uses a dedicated centralized config class (`FVP_IB_Strategy.Config.ProjectPaths`) exposing a static `BaseDirectory`. All File I/O across Indicators and Strategies strictly routes through this class.
* **Target Framework:** `.NET Framework / Quantower API v1.145.x`
* **Build Artifact Destination:** `C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\FVP_IB_Strategy.dll`
