# Quantower Master Reference Guide & Algorithmic Architecture

## 1. Order Routing & The Instant Fill Bug
* **The Problem:** If you default to `OrderTypeBehavior.Market`, or if you send a `Limit` sell order when the market price is above your entry price, the exchange fills it instantly at market price. This destroys backtesting PnL and trade reports.
* **The Solution:** Dynamically query `Core.Instance.OrderTypes` for `OrderTypeBehavior.Limit` or `OrderTypeBehavior.Stop` based on the market price relative to `EntryPrice`.
```csharp
OrderTypeBehavior requiredBehavior = OrderTypeBehavior.Limit;
if (side == Side.Buy && currentPrice < entryPrice) requiredBehavior = OrderTypeBehavior.Stop;
else if (side == Side.Sell && currentPrice > entryPrice) requiredBehavior = OrderTypeBehavior.Stop;
```

## 2. Stop Orders & The TriggerPrice Parameter
* **The Problem:** For Stop orders, `PlaceOrderRequestParameters` ignores the `Price` parameter. If you do not set `TriggerPrice`, the order fails or executes at market price.
* **The Solution:** Always populate `TriggerPrice` for Stop orders.
```csharp
Price = requiredBehavior == OrderTypeBehavior.Limit ? entryPrice : default(double),
TriggerPrice = requiredBehavior == OrderTypeBehavior.Stop ? entryPrice : default(double),
```

## 3. Backtesting PnL & ExitPrice Reconstruction
* **The Problem:** During Quantower backtesting, `Position.CurrentPrice` returns the end-of-simulation price (e.g., EOD price at 1:30 AM) when a position closes, rather than the actual price where the trade exited.
* **The Solution:** Never use `CurrentPrice` in backtest reports. Reconstruct `ExitPrice` mathematically using `GrossPnLTicks` or `Position.GetNetProfit()`.
```csharp
double exitPrice = entryPrice;
if (obj.Side == Side.Buy) exitPrice += (obj.GrossPnLTicks * obj.Symbol.TickSize);
else exitPrice -= (obj.GrossPnLTicks * obj.Symbol.TickSize);
```

## 4. Volume Profile "Smearing" & Timeframe Synchronization
* **The Problem:** Strategies hardcoded to `Period.MIN5` (5-minute bars) get fallback volume smeared across huge 5-minute candles. This smooths out valleys (LVNs) and prevents `BShape` (Double Distribution) detection, causing the Strategy to diverge from a 1-minute visual indicator.
* **The Solution:** 
  1. Force raw tick-level profile generation in backtesting by calling `Core.Instance.VolumeAnalysis.CalculateProfile(this.hdm);` in `OnRun()`.
  2. Expose `Timeframe Period` as an Input Parameter defaulting to `Period.MIN1`.

## 5. BShape Breakout Directional Logic
* **The Problem:** Sending a "BREAKOUT" order without explicit direction causes the execution engine to default to a Sell order. If the Take Profit was set using Long projection (`LVN + range * 0.5`), the Take Profit ends up above the short entry, resulting in an invalid or losing trade structure.
* **The Solution:** Dynamically determine direction by comparing `IB_HVN1` (POC) to `IB_LVN`. 
  * If `POC > LVN`: Go `SHORT`, Stop Loss at POC, Take Profit at `HVN2` (or project downwards).
  * If `POC < LVN`: Go `LONG`, Stop Loss at POC, Take Profit at `HVN2` (or project upwards).

## 6. Visual Debugging Lines & Microstructure Suggestions Across All Shapes
Drawing secondary distribution lines (`HVN2` in Orange dashed, `LVN` in Magenta dotted) provides immense visual clarity, but must be handled dynamically depending on the profile shape to avoid chart clutter:

* **`BShape` (Double Distribution):** **Always Draw.** The `LVN` is the exact valley where breakouts or rejections occur, and `HVN2` is the secondary target/magnet.
* **Other Shapes (`bShape`, `PShape`, `DShape`):** **Never Draw.** To maintain pristine charts and prevent visual clutter from minor statistical noise, hide secondary distribution lines entirely. VAH, VAL, and POC are the only necessary boundaries for single distributions.

## 7. Duplicate PositionRemoved Events
* **The Problem:** `Core.PositionRemoved` fires multiple times for the same position — once when SL/TP bracket orders are cancelled, and again when the position actually closes. This creates duplicate rows in CSV trade reports.
* **The Solution:** Maintain a `HashSet<string>` of processed position keys (`{obj.Id}_{obj.OpenTime.Ticks}`). Skip any position that has already been processed.

## 8. hasTradedToday Reset Across Backtest Days
* **The Problem:** If `hasTradedToday` only resets inside the IB phase time window (19:00-19:30), the backtester may skip/compress that window on some days, causing the flag to never reset and silently skipping entire trading days.
* **The Solution:** Reset `hasTradedToday` using date comparison (`istTime.Date != lastTradedDate`), not time-of-day phase detection.

## 9. EOD Flatten Must Use Market Orders
* **The Problem:** Using a Limit order to flatten positions at End-of-Day may never fill if the market moves away from the limit price.
* **The Solution:** Always use `OrderTypeBehavior.Market` for EOD flatten orders. Cache the Market order type ID at startup and fall back to Limit only if Market is unavailable.

## 10. Day-of-Week Column for Win Rate Filtering
* **Best Practice:** Always include a `Day` column (Monday, Tuesday, etc.) as the first column in trade report CSVs. This enables filtering by day of week to identify and skip low win-rate days, which is a critical edge optimization technique in systematic trading.

## 11. API Members That Do NOT Exist (v1.145.x)
The following members do NOT exist on the Quantower API and must never be used:
* `Position.GetNetProfit()` — Does not exist. Use `dynamic grossPnl = obj.GrossPnL; pnl = (double)grossPnl.Value;`
* `Position.ClosePrice` — Does not exist. Use `obj.CurrentPrice` as last resort only.
* `Symbol.TickCost` — Does not exist. Reverse-engineer from PnL using `pnl / obj.Quantity / tickSize`.

## 12. Architectural Selection: 1-Minute Time Bars vs. Tick Bars
* **1-Minute Time Bars (`Period.MIN1`):** Mandatory for time-bounded or Fixed Volume Profile strategies (e.g., Initial Balance 19:00–19:30 IST). Guarantees exact wall-clock cutoff precision without execution delays and prevents volume bleeding across session boundaries.
* **Tick Bars (e.g., 1000-Tick):** Not recommended for time-cutoff strategies due to boundary blurring (e.g., bar closing at 19:30:14). 
* **Future Tick-Bar Strategy Roadmap:** For detailed architectural blueprints on continuous Order Flow & Microstructure Scalping (Delta Imbalance, Pace of Tape, Adaptive Brackets), refer to [Future_Roadmap_OrderFlow.md](file:///c:/Surendran/Statergy%20devolpment/FVP_IB_Strategy/Future_Roadmap_OrderFlow.md).
