# Architectural Flaws

## Active Issues

### 1. Missing Dependency: System.Drawing.Common
- **File:** `IBVisualizerIndicator.cs`
- **Lines:** 342 - 350
- **Nature of Issue:** Structural / Project Configuration
- **Description:** The `IBVisualizerIndicator` component uses `System.Drawing` classes (`Pen`, `Font`, `SolidBrush`, `StringFormat`, `FontStyle`) for UI rendering. However, the project `FVP_IB_Strategy.csproj` is missing the required NuGet package reference to `System.Drawing.Common`. This breaks the build with CS1069 and CS0103 errors.
- **Status:** [RESOLVED] Added `System.Drawing.Common` package to `FVP_IB_Strategy.csproj`.
