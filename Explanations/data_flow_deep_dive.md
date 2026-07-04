# Deep Dive: The Data Flow Pipeline & The 2-Phase Lifecycle

This document explains exactly how data flows between the Quantower (QT) Indicator and the Cognee AI backend. 

To prevent spamming the AI and hitting rate limits, the lifecycle of a trade is strictly broken down into a **2-Phase Communication Model**. We do not stream data continuously; we only communicate with the AI at the very beginning (Query) and the very end (Ingestion) of a setup.

---

## The 2 Phases of a Trade Lifecycle

### Phase 1: Pre-Trade Query (Indicator ➔ AI ➔ Indicator)
* **What happens:** The live market finishes the Initial Balance (IB) period. The range and structural shape (e.g., bShape) are confirmed by the QT Indicator.
* **The Data Flow:** 
  1. QT Indicator sends the confirmed setup data to the AI backend (FastAPI `/analyze` endpoint).
  2. Cognee processes the setup against its historical graph memory.
  3. The AI returns a suggestion/score back to the QT Indicator to decide if the trade is worth taking.

### Phase 2: Post-Trade Ingestion (Indicator ➔ AI)
* **What happens:** The entire lifecycle of the setup terminates. During the active trade, **NO DATA** is sent to the AI. The system waits patiently until the outcome is final.
* **The Data Flow:** The QT Indicator takes the context from Phase 1 (The Setup) and combines it with the final execution outcomes (Triggered/Not Triggered, TP/SL/Cancelled, Final PnL) into one comprehensive payload. It sends this to the AI (FastAPI `/memory` endpoint) under three strict termination conditions:
  1. **Triggered and Closed:** Sent immediately after hitting Take Profit, Stop Loss, or EOD close.
  2. **Never Triggered:** Sent at session close (1:30 AM IST) when the pending limit order is cancelled.
  3. **Early Termination Safeguard:** If the user closes the trading platform or stops the strategy early, the C# strategy's `OnStop()` method catches the shutdown, cancels pending orders, and instantly flushes the final payload to the AI to prevent permanent memory loss.

---

## Behind the Scenes: What happens inside Cognee during Phase 1 and 2?

When QT sends data in **Phase 1 (Query)** or **Phase 2 (Ingestion)**, the Python backend must process it asynchronously so the Quantower charting thread never freezes:

1. **The Write-Ahead Log (WAL) & Queueing:** 
   - The Python server instantly writes the incoming JSON payload to a physical text file in the `IngestionQueue` folder and immediately returns a `200 OK` success response to Quantower. 
   - Even if the Python server crashes a millisecond later, the data is safely preserved on the hard drive, ensuring zero data loss.

2. **The Asynchronous Background Worker:**
   - A dedicated `asyncio` worker continuously monitors the queue, picking up one file at a time.
   - **Crucial Pacing:** After processing a file, the worker intentionally sleeps for 20 seconds. This is the "Cognee Multiplier," designed to protect against Gemini LLM Request-Per-Minute (RPM) rate limits and to prevent the graph database (Kuzu) from crashing due to concurrent file-lock collisions.

3. **Cognee Core Processing (The Database Stack):**
   - **SQLite (Metadata):** Cognee registers the payload, assigning a unique chunk ID and mapping its metadata.
   - **Gemini LLM (Extraction):** Cognee sends the raw JSON to the AI to extract entities (e.g., "Value Area High") and relationships (e.g., "Acted as Resistance").
   - **LanceDB (Vector Embedding):** The AI-extracted text is converted into numerical vectors (embeddings) and stored in LanceDB. This enables blazing-fast semantic similarity searches when Phase 1 asks, "Have we seen a shape like this before?"
   - **Kuzu (Knowledge Graph):** The extracted entities and relationships are physically mapped into a local graph database. This allows the AI to traverse multi-hop logical connections across historical trades, rather than just matching keywords.
