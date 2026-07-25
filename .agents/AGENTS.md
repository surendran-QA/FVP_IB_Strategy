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

## Release & DLL Management
When finalizing a branch (e.g., elease/phase1 or eature/phase1.1):
1. Ensure the project builds successfully.
2. Copy the resulting .dll file from the Quantower output directory and store it in the Releases/ folder at the root of this repository.
3. Rename the stored .dll to include the specific version (e.g., Releases/FVP_IB_Strategy_V1.dll or Releases/FVP_IB_Strategy_V1.1.dll) so multiple releases can be safely archived side-by-side in Git.
4. Keep the .csproj <AssemblyName> as FVP_IB_Strategy and <OutputPath> as the single active Quantower directory, meaning Quantower only ever runs the most recently compiled version, avoiding clutter.

## Version Naming Sync
When advancing to a new Phase of the roadmap (e.g., Phase 1 to Phase 2), you MUST explicitly update the internal C# `.Name` properties of all Strategies and Indicators to match the new version (e.g., "FVP IB Strategy V2"). This ensures the Quantower UI accurately reflects the active codebase phase.

## Git Workflow & Pushing
- **NEVER** run git push automatically after making commits. 
- You may commit changes locally to track progress, but you MUST wait for explicit user instruction before pushing any changes to a remote branch. The user will dictate exactly when and to which branch a push should occur.

## Quantower Market Replay & Indicator Simulation
- **Historical Array Indexing:** Quantower's `HistoricalData` is reverse-chronological. Index `0` is the NEWEST bar, and index `Count - 1` is the OLDEST bar. To loop through history forward in time (chronologically), you MUST iterate backwards: `for (int i = history.Count - 1; i >= 0; i--)`.
- **Replay Streaming:** In Market Replay, historical bars stream in tick-by-tick. If an Indicator runs a simulation (e.g., tracking trade execution) on historical data, it cannot just run once. It must continuously evaluate open states inside `OnUpdate()` because the "future" bars of that historical day haven't been loaded into the chart yet.
- **OHLC Bar Intra-Bar Simulation:** When using a custom `ExecutionSimulator` on OHLC bars, it is completely normal for Entry and Exit (TP/SL) to trigger on the exact same bar (and thus show the exact same timestamp) if the bar's High/Low encompasses both price levels. Always evaluate SL before TP for conservative backtesting.

## Cognee Ingestion & API Pacing (The Cognee Multiplier)
- **Never** execute `cognee.cognify()` directly within a fast ingestion endpoint (e.g., FastAPI route).
- **Always** decouple ingestion from processing:
  1. The endpoint must dump the payload into an `asyncio.Queue` (or a Write-Ahead Log folder) and immediately return a `200 OK` to prevent blocking the client.
  2. Implement a dedicated `asyncio.create_task()` Background Worker to consume the queue/log.
  3. The Background Worker MUST enforce a strict delay (e.g., `await asyncio.sleep(20)`) between processing each payload to account for the multiple LLM requests triggered by `cognify()` and remain safely under the LLM's RPM limits.

## AI Payload Lifecycle (The 2 Phases)
When managing a trade's lifecycle, the C# strategy must strictly limit its communication with the Cognee AI backend to a **2-Phase Model** to prevent API spam and rate limiting. Do not stream live trade data.

1. **Phase 1: Pre-Trade Query (`/analyze`)**
   - **Trigger:** Fired immediately when the Initial Balance (IB) range/shape is confirmed.
   - **Action:** Send the setup data to the AI to retrieve a probability score and trade suggestion.

2. **Phase 2: Post-Trade Ingestion (`/memory`)**
   - **Trigger:** Fired ONLY when the setup's entire lifecycle terminates. This occurs under three strict conditions:
     1. **Triggered Trade:** Sent immediately upon hitting Take Profit (TP), Stop Loss (SL), or an EOD force-close.
     2. **Un-Triggered Trade:** Sent at session close (1:30 AM IST) when pending limit orders are swept/cancelled.
     3. **Early Termination (Data Flush):** Sent if the user manually stops the strategy or closes Quantower early (caught via the `OnStop()` method).
   - **Action:** Combine the Phase 1 setup data with the final execution outcomes into a single payload, sending it to the AI for permanent graph ingestion.

## Repository Structure (Polyrepo Enforcement)
- **No Monorepos:** When developing external backend services (e.g., Python FastAPI servers, Cognee engines) that communicate with the Quantower Strategy, NEVER place them inside the C# Strategy directory. 
- **Isolation:** The C# root directory must remain strictly dedicated to Quantower code and its compilation output. External services must be created in sibling directories outside the C# root and tracked in their own separate Git repositories.
- **Hackathon Polyrepo Memory (CRITICAL):** The project is split into two distinct repositories that must be developed in parallel:
  1. **C# Trading Engine:** `C:\Surendran\Fixed volume profile with congee\FVP_IB_Strategy` (Active branch: `feature/phase2`)
  2. **Python Cognee AI Backend:** `C:\Surendran\Fixed volume profile with congee\FVP_IB_Cognee_Backend` (Active branch: `feature/phase2_congee`)
  Whenever modifying the Phase 2 AI integration, you MUST check BOTH folders to ensure the C# JSON payloads precisely match the Python Pydantic/FastAPI expected schemas.

<RULE[user_global]>
## Strict Version Control Guardrail
- NEVER execute `git commit` or `git push` commands automatically.
- Before committing or pushing any code to a repository, you MUST explicitly ask the user for permission and wait for their approval.
- You may still run safe, non-modifying commands like `git status` or `git diff` without asking.
</RULE[user_global]>


<RULE[user_global]>
## Databento Calendar Spread Corruption
When reading raw Databento trade files (`.dbn.zst`) into a Pandas DataFrame using `to_df()`, the file may contain multiple symbols, including Calendar Spreads (which trade at differential prices like 300.00 instead of 30,000.00). 
- If you do not filter these out, it will instantly corrupt High, Low, and Volume Profile calculations.
- **MANDATORY FIX:** You must ALWAYS filter the DataFrame to isolate the front-month outright contract by finding the symbol without a hyphen (`-`) that has the maximum volume, before performing any price analysis:
  `outright_symbols = df[~df['symbol'].str.contains('-')]`
  `front_month_symbol = outright_symbols['symbol'].value_counts().idxmax()`
  `df = df[df['symbol'] == front_month_symbol].copy()`
</RULE[user_global]>


<RULE[user_global]>
## Strict Forward-Looking Bias Prevention (The Time Wall)
When writing, refactoring, or optimizing backtesting engines (especially in Python or C#), you MUST mathematically segregate Indicator Generation from Trade Execution.
- **Phase 1 (Calculation):** All indicators (like Volume Profile, POC, Value Area, and Shapes) MUST be calculated using a strictly bounded chronological slice of data (e.g., `09:30 to 10:00`). You may never calculate these metrics using `rth_df` (the full session) if the strategy trades intra-day.
- **Phase 2 (Execution):** The execution simulator must start immediately *after* the calculation window ends (e.g., `10:01 to 16:00`). The simulator is strictly forbidden from accessing future data to alter the locked-in indicators.
- **Violations:** Breaking this rule causes Forward-Looking Bias, rendering the entire backtest mathematically invalid.
</RULE[user_global]>

