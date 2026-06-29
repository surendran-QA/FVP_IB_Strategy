# Phase 1 Restructuring and Bug Triage

## Architectural Alignment
Based on institutional quantitative standards, the monolithic codebase should be broken into a clean, OOP-compliant structure. Here is the proposed directory layout map:

`	ext
/FVP_IB_Strategy
¦
+-- /Calculations
¦   +-- DataIngestionService.cs
¦   +-- ExecutionSimulator.cs
¦   +-- InitialBalanceEngine.cs
¦   +-- ReportExporter.cs
¦   +-- ShapeDetector.cs
¦   +-- SignalGenerator.cs
¦   +-- VolumeProfileCalculator.cs
¦
+-- /Documentation
¦   +-- ARCHITECTURE.md
¦   +-- EXEC-BUGS.md
¦   +-- MEM-ENGINE.md
¦
+-- /Models
¦   +-- StructsAndEnums.cs
¦
+-- /Strategies
¦   +-- FVP_IB_Strategy.cs
¦
+-- /UI
    +-- /Indicators
        +-- IBVisualizerIndicator.cs
        +-- IBVisualizerIndicator_TV_Replica.cs
`

## Bug Triage (Diagnostics Only)

**1. The Daylight Saving Time (DST) Trap (Hardcoded IST vs. dynamic EST/EDT)**
- **Diagnostic:** Using a static time offset or hardcoded timezone calculation causes synchronization failure during the EDT/EST crossover.
- **Triage Status:** The strategy must use .NET TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time") natively to account for dynamic shifts. 

**2. The Ghost Filter Look-Ahead Bias (Using Close instead of High/Low for SL/TP)**
- **Diagnostic:** Evaluating ar.Close against target levels allows intra-bar sweeps to remain invisible to the backtester, inflating win rates.
- **Triage Status:** The execution logic must check ar.Low against Stop Loss and ar.High against Take Profit (for Longs).

**3. The Value Area Tie-Breaker Crash (No directional bias for equal volume bins)**
- **Diagnostic:** When expanding the Value Area from the POC, an exact volume tie between the upper and lower bins can stall the mathematical loop.
- **Triage Status:** A deterministic fallback (e.g., explicitly favoring upward expansion on a tie) must be injected into the Volume Profile loop.

**4. The B-Shape LVN Execution Slippage (Targeting zero-liquidity nodes)**
- **Diagnostic:** Generating limit orders precisely inside a Low Volume Node (LVN) forces execution in an "air pocket," causing slippage in live trading.
- **Triage Status:** Entry prices must be shifted by one inOffset toward the adjacent High Volume Node (HVN).

---

## Authorization Prompt

**Awaiting Commander Approval:**
Please review the proposed architectural layout and the bug triage diagnostics above. Do you authorize Antigravity to proceed with executable code refactoring and directory restructuring?
