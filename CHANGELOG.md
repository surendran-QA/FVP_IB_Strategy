# Changelog

All notable changes to this project will be documented in this file.

## [v1.1] - 2026-06-27
### Added
- Created `SignalGenerator.cs` to mathematically deduce trade signals (Preferred Side, Entry, Take Profit, Stop Loss) from the IB volume profile shape.
- Added visual `Live Info Table` to `IB Visualizer v1.1` and `IB Visualizer (TV Replica) v1.1` for displaying live day trade signal status.
- Added historical floating text overlays above/below the range box to display `TradeSignal` data for past profiles without obstructing price action.
- Added UI checkboxes `Show Live Info Table` and `Show Historical Info Text` for quick visibility toggling.

## [v1.0] - Initial Structure
### Added
- Created `Documentation/` directory with `ARCHITECTURE.md`, `MEM-ENGINE.md`, `ROADMAP.md`, `ARCH-FLAWS.md`, `EXEC-BUGS.md`.
- Created `Models/`, `Calculations/`, `Strategies/`, `Execution/`, and `UI/Indicators/` directories.
- Extracted `Enums.cs` (StopLossTypeEnum, TakeProfitTypeEnum, VolumeProfileShape) to `Models/Enums.cs`.
- Extracted `ShapeDetector.cs` logic to `Calculations/ShapeDetector.cs` for volume profile shape classification.
- Extracted `InitialBalanceEngine.cs` to `Calculations/InitialBalanceEngine.cs` for IB profiling logic.
- Extracted `TradingContext.cs` to `Models/TradingContext.cs` to encapsulate order parameters.
- Extracted `OrderManager.cs` to `Execution/OrderManager.cs` to isolate place order execution logic.
- Extracted `TvReplicaBalanceEngine.cs` to handle absolute-zero alignment binning logic for TV Replica indicator.

### Changed
- Removed extracted Enums from `FVP_IB_Strategy.cs`.
- Refactored `FVP_IB_Strategy.cs` into an orchestrator logic.
- Relocated `FVP_IB_Strategy.cs` to `Strategies/`.
- Relocated indicator files to `UI/Indicators/`.
- Refactored `IBVisualizerIndicator.cs` to use `InitialBalanceEngine`.
- Refactored `IBVisualizerIndicator_TV_Replica.cs` to use `TvReplicaBalanceEngine`.

### Fixed
- Added missing `System.Drawing.Common` dependency to resolve compilation flaw in UI components.
