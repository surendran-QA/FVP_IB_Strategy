# EXEC-BUGS: Replay-Mode Vulnerabilities

## 1. The Profile Corruption Bug (Session Reset Failure)
**Status:** TRIAGED | **Severity:** CRITICAL
**Issue:** 
The `InitialBalanceEngine` (or `TvReplicaBalanceEngine`) does not explicitly wipe its arrays, dictionaries (e.g., volume profile tracking), and struct variables at the end of a trading session. During continuous multi-day replay, the volume from Day 1 will bleed into Day 2, irreparably corrupting the POC, VAH, and VAL calculations for all subsequent days.
**Required Architecture:** 
The orchestrator must track the current bar's Date (`bar.Time.Date`) and explicitly trigger a complete data wipe (resetting the engine's state) the moment the date changes or when the clock hits `EndTradingTime` (e.g., 16:00 EST).

## 2. The AI Memory Sequencing Flaw (Time-Stamp Override)
**Status:** TRIAGED | **Severity:** CRITICAL
**Issue:** 
Generating JSON payloads using local machine time (e.g., `DateTime.Now` or `DateTime.UtcNow`) during a backtest/replay will tag all historical payloads with the present real-world time. This destroys the chronological integrity of the events, causing the AI Knowledge Graph to sequence 5 days of history as occurring simultaneously at the exact moment the backtest was run.
**Required Architecture:** 
All network payloads formatting text for Cognee must explicitly extract and inject the historical simulation time (`bar.Time` converted to EST), bypassing the machine's local clock entirely.

## 3. The Replay Mode Deadlock (C# Semaphore Backpressure)
**Status:** NEW | **Severity:** CRITICAL
**Issue:** In `CogneeIntegrationService.cs`, `_memoryThrottle` blocks thread execution during Payload 1. Because the Python backend (Llama 3.3) takes ~40s, Payload 2 queues behind the lock. If Quantower stops the replay rapidly, the background task is aborted and Payload 2 is assassinated before being sent, causing silent memory loss.
**Required Architecture:** Decouple Payload 1 and Payload 2 throttling, or implement a durable local WAL (Write-Ahead Log) queue in C# before attempting network egress.

## 4. Python Async Queue Graceful Shutdown
**Status:** NEW | **Severity:** MEDIUM
**Issue:** `payload_queue` in `server.py` paces via `await asyncio.sleep(20)`. If the Python server shuts down ungracefully, queued payloads might be abandoned if the WAL recovery fails edge cases.
**Required Architecture:** Implement signal handlers (`SIGTERM`, `SIGINT`) in FastAPI to flush the queue gracefully before exiting.
