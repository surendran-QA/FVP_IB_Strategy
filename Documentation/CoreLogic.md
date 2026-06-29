# FVP Initial Balance Strategy & Core Logic

This document serves as the master reference for the mathematical and structural mechanics of the Fixed Volume Profile (FVP) and Initial Balance (IB) Strategy.

## 1. Core Profile Math & Data Aggregation
The Volume Profile is constructed over a precise 30-minute window (e.g., 19:00 to 19:30).

### Issue with Standard 1-Minute Data
When using basic `Period.MIN1` history, the algorithm has to look at the High, Low, and Volume of the 1-minute candle, and distribute the volume evenly across all price levels within that candle.
- **Formula:** `volumePerTick = TotalVolume / (Math.Round((High - Low) / TickSize) + 1)`
- **Drawback:** If a massive amount of volume happens exactly at one specific price (like a massive rejection wick), the 1-minute math dilutes that volume across the entire 50-point length of the candle. This results in an inaccurate Point of Control (POC).

### The Solution: IVolumeAnalysisIndicator
By having the Strategy or Indicator implement the `IVolumeAnalysisIndicator` interface, Quantower's background engine automatically fetches exact tick-level Volume Analysis data from the broker.
- **Usage:** We iterate over `bar.VolumeAnalysisData.PriceLevels`
- **Benefit:** This gives the exact volume traded at exactly 29,326.00, resulting in a POC that is mathematically identical to the built-in Custom Profile tool.

## 2. Bin Alignment (Custom Step Math)
When aggregating ticks into larger bins (e.g., Custom Step = 4 ticks), the mathematical starting point of the bins heavily influences where the POC lands.

### Absolute Zero Alignment (TradingView Replica)
Bins are grouped starting from absolute $0.00.
- **Formula:** `Math.Floor(price / binSize) * binSize`
- **Result:** Bins land on absolute numbers like 29,320.00, 29,321.00. This matches how TradingView calculates its fixed profiles.

### Absolute Low Alignment (Quantower Built-in)
Bins are grouped starting exactly from the lowest traded price (`IB_Low`) of that specific profile period.
- **Formula:** `Math.Floor((price - IB_Low) / binSize) * binSize + IB_Low`
- **Result:** If `IB_Low` is 29,243.00, the first bin is 29,243.00 to 29,244.00. This is the math required to identically clone the Quantower built-in profile tool.

## 3. Shape Classification Logic
Once the profile is calculated, it is classified based on the location of the POC relative to the range (`IB_High - IB_Low`).
- **P-Shape:** POC is in the top 40% of the profile. (Bullish skew)
- **b-Shape:** POC is in the bottom 40% of the profile. (Bearish skew)
- **D-Shape:** POC is in the middle. (Balanced)
- **B-Shape (Double Distribution):** Contains two distinct High Volume Nodes (HVNs) separated by a Low Volume Node (LVN) whose volume is < 20% of the POC volume, and the distance between HVNs is >= `DoubleDistMinTicks`.
