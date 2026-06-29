## Quantower Backtesting & PnL
- When logging time in a backtest, use 	his.CurrentTime or obj.CloseTime.
- To extract PnL from a Position object, use dynamic conversion (dynamic grossPnl = obj.GrossPnL; double rawPnl = (double)grossPnl.Value;) as obj.NetPnL returns a PnLItem. Do NOT use GetNetProfit().

## UI Feedback during Historical Batch Processing
- When writing indicators or strategies that loop over historical data and output a single UI status or debug string to the chart, remember to reset or clear the status string at the start of every new day/session.

## External DLL Deployment Cleanup
- When compiling a Quantower project externally and deploying the .dll, NEVER allow .cs files to exist in the target Quantower folders to prevent conflicting internal compilation.
- Before external deployment, aggressively clear the target Quantower Strategies and Indicators folders of everything except the expected output DLLs.

## Limit Order Type Bug
- When initializing a strategy that uses limit orders, ensure you explicitly query for OrderTypeBehavior.Limit. If you default to OrderTypeBehavior.Market, the backtester will fill orders instantly at market price, ruining EntryPrice, ExitPrice, and PnL calculations.

## FVP_IB_Strategy Development Roadmap
The project follows a strict 4-phase development roadmap. Agents must adhere to the current phase constraints and avoid feature creep from future phases.

### Phase 1: The IB-Only Baseline (Completed)
- **Objective:** Establish a mathematically sound, functional foundation without feature creep.
- **Constraints:** Execute trades exclusively on 30-min IB shapes (PShape, bShape, DShape, BShape). Use strictly structural stop-losses tied to opposing Value Area extremes or HVNs.

### Phase 2: The Cognee AI Memory Integration (Current Focus)
- **Objective:** Connect the rock-solid C# foundation to the AI brain.
- **API Architecture:** Build C# HTTP request logic to extract the exact market state at execution.
- **Payload Structuring:** Format temporal data, asset identity, microstructure state, and trade outcomes into standardized JSON.
- **Knowledge Graphing:** Feed raw text summaries into the Cognee backend to store structural data, track inter-market correlations, and recognize patterns as a second memory layer.

### Phase 3: Footprint & Order Flow Logic (Deferred)
- **P3.1 (Sandbox Engine):** Develop and test Cumulative Volume Delta (CVD) and order flow reading in an isolated environment.
- **P3.2 (Final Integration):** Merge footprint logic into the live strategy to filter trades based on real-time aggressive buying/selling at structural nodes.

### Phase 4: Macro Filters & Advanced Risk (Deferred)
- **The Macro Filter:** Inject Previous Day RTH and Overnight Session profile logic.
- **The Advanced Risk Engine:** Introduce dynamic ATR stops and fixed-tick stop configurations.

## Architectural Refactoring & Parameter Changes
When the user requests a change to a foundational piece of logic or a hardcoded constraint (e.g., changing timeframes, IB duration, or execution logic):
1. **Mandatory Impact Audit:** You MUST perform a global grep_search across the entire codebase to identify all affected files, variables, UI components, and visualizers.
2. **Pre-Execution Listing:** Before writing any code, you MUST list out every affected area in an implementation_plan.md and explicitly await user approval.
3. **No Blind Updates:** Do not assume a change only affects the core engine; always verify the UI/Indicators and downstream simulator dependencies.
