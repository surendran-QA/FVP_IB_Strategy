# FVP IB Strategy: Architectural Overview

## System Design Philosophy
The system is built on a decoupled, event-driven architecture designed to separate latency-sensitive trade execution from compute-heavy knowledge graph generation.

### Component 1: Edge Execution (C# / Quantower)
The trading terminal runs a compiled `.dll` within the Quantower environment. It operates purely on structural market data (Volume Profile, Initial Balance).
- **Resilience:** If the AI backend goes down, the C# engine continues trading on its mathematically proven Phase 1 structural logic without failure. 

### Component 2: The Ingestion Pipeline (Python / FastAPI)
The bridge between C# and the AI is handled by a local FastAPI instance.
- **Write-Ahead Log (WAL):** When an HTTP POST is received, the payload is immediately serialized to a JSON file on disk before queuing. This ensures zero data loss if the Python server crashes mid-process.
- **Paced Asynchronous Queue:** A background worker reads from the queue. Because Kuzu (the graph database) relies on strict file-based locks and is not concurrency-safe, the queue forces strict sequential processing. A 20-second sleep is enforced per item to accommodate LLM token limits and prevent race conditions.

### Component 3: The Memory Engine (Cognee)
Cognee orchestrates the dual-memory retrieval system.
1. **Relational (SQLite):** Acts as the system catalog.
2. **Vector (LanceDB):** Handles unstructured semantic search. When the C# strategy asks, "Have we seen a setup like this recently?", LanceDB calculates cosine similarity on the vectorized market context.
3. **Graph (Kuzu):** Handles structured reasoning. The LLM extracts nodes (e.g., Support, Resistance, Value Area High) and edges, allowing the system to query multi-hop logical relationships that a standard vector database cannot achieve.

### Scalability Roadmap (Phases 3 & 4)
Currently, the system is designed for single-user, local-machine execution. 
If scaled to a multi-agent or cloud deployment, the Kuzu file-based graph will be swapped for a containerized **Neo4j** instance, and the SQLite metadata store will migrate to **PostgreSQL**. The ingestion queue can be smoothly migrated from `asyncio.Queue` to **Redis** or **RabbitMQ** with minimal code churn.
