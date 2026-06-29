# FVP IB Strategy: Quantitative Trading Logic Master Reference

This document isolates the pure algorithmic and quantitative trading rules executed by the `FVP_IB_Strategy`. It strips away software architecture to focus strictly on market microstructure analysis, shape detection, and trade execution rules.

## 1. Core Operating Window
The strategy analyzes the **Initial Balance (IB)** period of the US Equity markets.
* **Calculation Window:** `09:30:00 EST` to `10:00:00 EST` (First 30 minutes of the regular session).
* **Data Processing:** All volume executed during this 30-minute window is binned into a Fixed Volume Profile (FVP) using a predefined tick step (e.g., 4 ticks = 1 point).
* **Value Area:** The algorithm locates the Point of Control (POC), then expands outward to encompass exactly `70%` of the total IB volume, establishing the Value Area High (VAH) and Value Area Low (VAL).

---

## 2. Shape Detection & Market Profiling

The volume profile is algorithmically classified into one of four standard market shapes based on the location of the POC relative to the total IB Range (High to Low).

### A. The P-Shape (Short Covering / Bullish)
* **Definition:** The highest volume node (POC) is established in the upper portion of the profile.
* **Mathematical Gate:** `POC > (IB_Low + 0.6 * IB_Range)` (POC is in the top 40% of the range).

### B. The b-Shape (Long Liquidation / Bearish)
* **Definition:** The highest volume node (POC) is established in the lower portion of the profile.
* **Mathematical Gate:** `POC < (IB_Low + 0.4 * IB_Range)` (POC is in the bottom 40% of the range).

### C. The D-Shape (Normal / Range Bound)
* **Definition:** The volume is evenly distributed in a bell curve.
* **Mathematical Gate:** The POC sits exactly in the middle 20% of the range (between the 40% and 60% thresholds).

### D. The B-Shape (Double Distribution / Breakout)
* **Definition:** The market attempted to find balance at two different price levels, creating two distinct High Volume Nodes (HVN1 and HVN2) separated by a distinct Low Volume Node (LVN) or liquidity vacuum.
* **Mathematical Gates (Strict Qualification):**
  1. The secondary peak (`HVN2`) must be separated from `HVN1` (the POC) by a minimum tick distance (e.g., 40 ticks / 10 points).
  2. **Peak Significance:** The volume at `HVN2` must be at least `30%` of the volume at the primary POC.
  3. **Valley Thinness:** The lowest volume bin (`LVN`) situated directly between `HVN1` and `HVN2` must hold less than `12%` of the primary POC's volume.

---

## 3. Trade Execution Logic & Risk Management

Once the profile shape is locked at exactly `10:00:00 EST`, the `SignalGenerator` converts the shape into strict, risk-defined bracket orders (Entry, Take Profit, Stop Loss).

### Trade Setup 1: B-Shape (Double Distribution Momentum Breakout)
The strategy assumes the market will reject the `LVN` vacuum and accelerate toward the opposite distribution.
* **Directional Logic:**
  * If `POC` is ABOVE the `LVN` (Breaking Down): **SHORT** Entry.
  * If `POC` is BELOW the `LVN` (Breaking Up): **LONG** Entry.
* **Execution Coordinates:**
  * **Entry:** `LVN` ± 1 Bin Offset (We mathematically offset the limit order by 1 bin toward the POC to guarantee a fill against the thick HVN wall, preventing slippage inside the zero-liquidity LVN vacuum).
  * **Stop Loss:** `HVN1` (The primary POC).
  * **Take Profit:** `HVN2` (The secondary peak). If `HVN2` is undefined due to extreme imbalance, the target is projected algorithmically `LVN ± (Range * 0.5)`.

### Trade Setup 2: P-Shape (Mean Reversion / Trend Continuation)
* **Direction:** **LONG**
* **Entry:** `POC`
* **Stop Loss:** `VAL` (Value Area Low - If price breaks below the 70% value area, the bullish thesis is invalidated).
* **Take Profit:** `IB_High` (Targeting the top of the initial balance range).

### Trade Setup 3: b-Shape (Mean Reversion / Trend Continuation)
* **Direction:** **SHORT**
* **Entry:** `POC`
* **Stop Loss:** `VAH` (Value Area High - If price breaks above the 70% value area, the bearish thesis is invalidated).
* **Take Profit:** `IB_Low` (Targeting the bottom of the initial balance range).

### Trade Setup 4: D-Shape (Range Bound FADE)
* **Direction:** **FADE (Mean Reversion LONG)**
* **Entry:** `VAL` (Fading the extreme edge of the value area).
* **Stop Loss:** `IB_Low` (If the absolute low breaks, the range-bound thesis is dead).
* **Take Profit:** `POC` (Returning to the mean/center of the bell curve).

---

## 4. End of Day Flatten
* **Rule:** No positions are held overnight.
* **Action:** At the designated `EndTradingTime` (e.g., 01:30 IST / late US session), all pending limit/stop orders are cancelled. Any active, open positions are instantly flattened using an un-cancellable **Market Order** to guarantee zero overnight risk regardless of high volatility.
