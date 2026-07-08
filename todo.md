# FVP IB Strategy - Future TODO List

## Architecture
- [x] Separate the `IBVisualizerIndicator` and `FVP_IB_Strategy` into two completely distinct C# files/projects to keep the codebase cleaner.
- [x] Create a TradingView Replica indicator backup file (`IBVisualizerIndicator_TV_Replica.cs`).
- [x] Create `CoreLogic.md` to document the Volume Analysis extraction and aggregation math.

## Execution Logic & Trading Features
- [ ] Implement Target Profit (TP) limit orders. (Currently SL is visible/implemented but TP logic needs visual/execution parity).
- [ ] Implement Previous Day Volume Profile Shape Recognition (P, b, D, B) as detailed in the original design specs.
- [ ] Store Previous Day RTH metrics (PrevDay_High, PrevDay_Low, PrevDay_POC, CurrentShape).
- [ ] Calculate previous day shape dynamically using the 9:30 AM to 4:00 PM EST (RTH) window.
- [ ] Enhance AI Payload: Extract the precise Volume traded exactly at the Entry Price (from the IB Volume Profile) and append it to the `CogneeIntegrationService` payload so the AI has market acceptance context.

## Indicators & Visuals
- [x] Make the visualizer draw the previous day's IB profile instead of just the current session (Added "Show Historical Profiles" Checkbox).
- [ ] Add options to hide/show specific elements (e.g., Hide POC, Hide VAH/VAL) inside the indicator properties.
- [ ] Prototype an Anchored VWAP (AVWAP) mathematical calculator in a standalone C# Console App, verify the math, and then integrate it into the `FVP_IB_Strategy`.
