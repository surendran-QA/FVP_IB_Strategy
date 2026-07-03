# Architectural Flaws

## Active Issues

### 1. Missing Dependency: System.Drawing.Common
- **File:** `IBVisualizerIndicator.cs`
- **Lines:** 342 - 350
- **Nature of Issue:** Structural / Project Configuration
- **Description:** The `IBVisualizerIndicator` component uses `System.Drawing` classes (`Pen`, `Font`, `SolidBrush`, `StringFormat`, `FontStyle`) for UI rendering. However, the project `FVP_IB_Strategy.csproj` is missing the required NuGet package reference to `System.Drawing.Common`. This breaks the build with CS1069 and CS0103 errors.
- **Status:** [RESOLVED] Added `System.Drawing.Common` package to `FVP_IB_Strategy.csproj`.

### 2. The Cognee Multiplier Flaw
- **Nature of Issue:** API Rate Limit Exhaustion / Architecture Mismatch
- **Description:** When a single payload is ingested via `cognee.add()` and processed via `cognee.cognify()`, it triggers a cascade of LLM calls (e.g., Entity Extraction, Relationship Extraction, Chunk Summarization). Thus, 1 Payload = Multiple LLM Calls. If the C# backtester sends multiple payloads rapidly, it instantly overwhelms the 15 RPM limit of Gemini 3.1 Flash Lite.
- **The Fix:** Decouple Ingestion from Processing. The `/memory` endpoint must instantly acknowledge the payload and dump it into an `asyncio.Queue`. A dedicated Background Worker must slowly consume this queue, enforcing a strict `asyncio.sleep(20)` delay between each `cognify()` execution to mathematically guarantee the RPM limit is never breached.
