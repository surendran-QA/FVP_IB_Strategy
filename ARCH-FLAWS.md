# ARCH-FLAWS: System Architecture Vulnerabilities

## 1. The Asynchronous Speed Trap (Network Backpressure)
**Status:** TRIAGED | **Severity:** HIGH
**Issue:** 
Firing non-blocking `PostAsync` HTTP requests over a local REST API during a 50x Market Replay acts as a Denial of Service attack on the Python server. The fast-streaming replay loop launches asynchronous tasks faster than the network/server can process them. Because the C# thread does not wait for a response, the sequence of HTTP packets can arrive out of order, or the local port can become bottlenecked, leading to chaotic race conditions where later logs resolve before earlier ones.
**Required Architecture:** 
We must introduce a synchronous throttle or an `await` lock pattern (e.g., a semaphore or blocking queue) specifically designed for the `MemoryBridgeService.cs` (or equivalent HTTP service) whenever the system detects it is operating in Replay or Backtest mode. This ensures chronological integrity of the Memory Engine.
