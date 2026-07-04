# FVP IB Strategy: Developer Overview

## Project Architecture & Stack
The `FVP_IB_Strategy` is a hybrid algorithmic trading system that bridges a high-performance C# execution engine with an asynchronous Python AI backend.

### 1. The C# Execution Engine (Quantower)
- **Framework:** Quantower Trading API (.NET / C#).
- **Core Logic (Phase 1):** The C# side is strictly responsible for fast, mathematically sound market execution. It analyzes 30-minute Initial Balance (IB) shapes (PShape, bShape, DShape, BShape) and structural nodes (Value Area extremes, HVNs). 
- **Rule:** It handles limit order placement, stop-loss structural trailing, and charting UI. It does **not** do heavy computation or AI logic directly to avoid blocking the main UI/execution thread.

### 2. The Python AI Backend (FastAPI + Cognee)
- **Framework:** Python, FastAPI, Uvicorn, Cognee.
- **Integration (Phase 2):** When the C# strategy detects a setup or completes a trade, it formats the exact market state (microstructure, asset identity, time) into a standardized JSON payload and fires an HTTP POST request to the Python server's `/memory` endpoint.
- **Asynchronous Design:** The FastAPI endpoint instantly dumps the payload to disk (Write-Ahead Log) and returns a `200 OK` to ensure the C# thread is immediately freed up. A dedicated `asyncio` background worker consumes the queue.

### 3. AI & Memory Processing
- The background worker passes the payload to **Cognee**.
- **LLM Integration:** Cognee uses LLMs (like Gemini) to extract text-based entities and structural relationships from the trade payload.
- **The Local Database Stack:**
  - `SQLite`: Manages metadata and file chunk registries.
  - `LanceDB`: Stores vectorized text (embeddings) for fast semantic similarity search.
  - `Kuzu`: A local graph database that maps structural relationships (e.g., `[IB High] --ACTED_AS--> [Resistance]`).

## Development Guidelines
- **No UI Blocking:** Never run heavy historical batch processing or AI API calls on the C# UI thread. Always use asynchronous webhooks.
- **Rate Limiting:** The Python background worker has a strict `asyncio.sleep(20)` delay between processing queue items. This is the "Cognee Multiplier" designed to protect against LLM RPM (Requests Per Minute) limits and to prevent concurrent write crashes in Kuzu.
