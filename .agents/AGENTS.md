## Quantower Backtesting & PnL
- When logging time in a backtest, use `this.CurrentTime` or `obj.CloseTime`.
- To extract PnL from a `Position` object, use `obj.GetNetProfit()` or dynamic conversion as `obj.NetPnL` returns a `PnLItem`.

## UI Feedback during Historical Batch Processing
- When writing indicators or strategies that loop over historical data and output a single UI status or debug string to the chart, remember to reset or clear the status string at the start of every new day/session.

## External DLL Deployment Cleanup
- When compiling a Quantower project externally and deploying the .dll, NEVER allow .cs files to exist in the target Quantower folders to prevent conflicting internal compilation.
- Before external deployment, aggressively clear the target Quantower Strategies and Indicators folders of everything except the expected output DLLs.

## Limit Order Type Bug
- When initializing a strategy that uses limit orders, ensure you explicitly query for OrderTypeBehavior.Limit. If you default to OrderTypeBehavior.Market, the backtester will fill orders instantly at market price, ruining EntryPrice, ExitPrice, and PnL calculations.
