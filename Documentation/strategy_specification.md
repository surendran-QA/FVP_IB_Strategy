# FVP Initial Balance Strategy Specification

This document outlines the complete logic, mechanics, and architecture of the **Fixed Volume Profile (FVP) Initial Balance (IB)** trading strategy developed for the Quantower platform. You can provide this specification to AI models (like Gemini) to help build expansions, ports to other platforms (like TradingView/PineScript), or secondary variations.

---

## 1. Core Concept & Timings
The strategy operates on the concept of capturing the "Initial Balance" (the first 30 minutes of a specific trading session) and generating a Fixed Volume Profile (FVP) exclusively for that 30-minute window. Based on the statistical shape of that volume profile, the algorithm determines directional bias, precise entry levels, and dynamic stop-loss/take-profit targets.

- **Base Timeframe:** 5-Minute Candles (`Period.MIN5`)
- **Profile Calculation Granularity:** Uses Tick Data or 1-Minute Data (based on broker feed) injected into `Profile Step Ticks` (default: 4 ticks) to build an ultra-precise volume histogram.
- **IB Phase:** 19:00 IST to 19:30 IST. 
- **Execution Phase:** 19:30 IST to 01:30 IST.
- **End of Day (EOD) Flattening:** 01:30 IST.

---

## 2. The Volume Profile (The IB Phase)
During the 19:00 - 19:30 window, the strategy calculates the following structural nodes:
*   **POC (Point of Control):** The price level with the absolute highest traded volume.
*   **VAH (Value Area High) & VAL (Value Area Low):** The upper and lower bounds containing exactly 70% of the total traded volume during the IB phase.
*   **IB High & IB Low:** The absolute highest and lowest prices traded during the 30-minute window.

---

## 3. Shape Classification Algorithm
At exactly 19:30 IST, the strategy analyzes the distribution of the volume histogram to classify it into one of four distinct shapes:

1.  **pShape (Bullish Accumulation):**
    *   *Condition:* The POC is located in the upper 50% of the IB range (closer to IB High).
    *   *Meaning:* Buyers stepped in aggressively late in the session, shifting the bulk of volume higher.

2.  **bShape (Bearish Distribution):**
    *   *Condition:* The POC is located in the lower 50% of the IB range (closer to IB Low).
    *   *Meaning:* Sellers dominated, pushing the heaviest volume node towards the bottom of the range.

3.  **DShape (Balanced / Range-Bound):**
    *   *Condition:* The POC is dead center (middle 30-40% of the range), with volume tapering off symmetrically towards the High and Low.
    *   *Meaning:* Perfect equilibrium between buyers and sellers. 

4.  **BShape (Double Distribution):**
    *   *Condition:* There are two distinct High Volume Nodes (HVN1 and HVN2) separated by a clear Low Volume Node (LVN), and the distance between the HVNs is greater than the `Double Dist Min Ticks` parameter.
    *   *Meaning:* Price violently rejected a middle zone, creating two separate areas of acceptance.

---

## 4. Signal Generation (Trade Logic)
Once the shape is determined, the `SignalGenerator` class derives the exact mathematical parameters for the trade.

### **Rule 1: pShape (Bullish Bias)**
*   **Side:** LONG
*   **Entry Price:** At the **POC**.
*   **Stop Loss:** Placed just below the **VAL** (with a small buffer).
*   **Take Profit:** 1.5x to 2x the Risk (Entry - SL), projecting upwards.

### **Rule 2: bShape (Bearish Bias)**
*   **Side:** SHORT
*   **Entry Price:** At the **POC**.
*   **Stop Loss:** Placed just above the **VAH** (with a small buffer).
*   **Take Profit:** 1.5x to 2x the Risk (SL - Entry), projecting downwards.

### **Rule 3: DShape (Mean Reversion / Fade)**
*   **Side:** FADE (Wait for extremes)
*   **Setup:** Since it's a balanced day, the strategy assumes price will revert to the mean (POC).
*   **Short Entry:** At the **VAH**. (Stop Loss above IB High).
*   **Long Entry:** At the **VAL**. (Stop Loss below IB Low).
*   *Note: In the automated strategy, DShape is currently configured to wait for false breakouts or is filtered out depending on strict risk parameters.*

### **Rule 4: BShape (Double Distribution)**
*   **Side:** FADE The LVN (Low Volume Node).
*   **Setup:** Price tends to reject the LVN gap.
*   **Short Entry:** If price pushes up into the LVN from below, short it.
*   **Long Entry:** If price pushes down into the LVN from above, buy it.

---

## 5. Automated Execution Engine (`OrderManager`)
*   **Trigger Time:** Exactly at 19:30 IST.
*   **Order Type:** It places a **Day LIMIT Order** resting exactly at the `EntryPrice` derived by the Signal Generator.
*   **Risk Management:** The strategy utilizes the broker's **OCO (One-Cancels-Other)** bracket functionality to attach the mathematical Stop Loss and Take Profit to the resting Limit Order. 
*   **Waiting Phase:** The strategy passively waits. If the price never retraces to the `EntryPrice` limit order, no trade is taken.

---

## 6. End of Day (EOD) Operations
At 01:30 IST, the Execution Engine triggers the EOD sequence:
1.  **Cancel Pending:** If the Limit Order was never filled (price never hit our entry), the order is cancelled.
2.  **Flatten Open Positions:** If we are actively in a trade that hasn't hit SL or TP yet, the strategy immediately fires a **Market Order** to close the position and flatten the account, preventing overnight exposure.

---

## 7. Metrics & Logging
*   **In-Platform:** Tracks Total Trades, Win/Loss count, Win Rate %, and Net PnL directly in the Quantower Strategy Panel.
*   **CSV Reporting:** Hooks into the `PositionRemoved` event. Every time a trade closes, it appends a row to a local file (`TradeReport.csv`) logging the Execution Time, Direction, Entry Price, Exit Price, Net PnL, and the specific Volume Profile Shape that initiated the trade. 
