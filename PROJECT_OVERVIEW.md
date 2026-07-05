# Project Overview: Fixed Volume Profile (FVP) AI Trading System

## 🎯 Our Motto & The Problem We Are Fixing

**The Problem:** 
* **Rigid Rules:** Traditional trading bots blindly execute trades based on hard-coded mathematical rules.
* **The Retail Trader Trap:** Retail traders often rely heavily on basic indicators. These indicators only calculate raw math (like averages or volume) but cannot perform actual contextual market analysis.
* **No Memory:** When market conditions shift, these rigid strategies fail. They keep losing money because they have no "memory" of past mistakes or successes to learn from.

**The Solution (The "Brain"):** We are fixing this by giving the trading engine a cognitive memory graph. We combine a high-performance C# execution engine (which analyzes Fixed Volume Profile and Initial Balance) with a Python-based AI Backend powered by Cognee and Gemini. 

Instead of just blindly executing trades, the system sends the current market structure to the "Brain." The Brain queries its historical graph database of past trades, evaluates the current setup against past outcomes using an LLM, and calculates a dynamic win probability. If the probability is too low, the Brain vetoes the trade. When a trade finishes, the outcome is fed back into the Brain, making the system smarter and more resilient every single day.

---

## 🏗️ The Two Separate Projects

To maintain strict deployment architecture and separation of concerns, the ecosystem is split into two distinct repositories:

### 1. `FVP_IB_Strategy` (C# - The Execution Engine)
* **What it is:** A Quantower automated strategy and visual indicator.
* **Responsibilities:**
  * Connects to live broker data feeds and manages order execution.
  * Mathematically calculates the Initial Balance (IB) and Fixed Volume Profile (FVP) structures (e.g., Value Area High/Low, Point of Control, Profile Shapes).
  * Suspends execution to query the AI Brain via HTTP webhooks before placing an order.
  * Records trade outcomes (Take Profit, Stop Loss, Flat) and structures this complex data into readable JSON payloads.

### 2. `FVP_IB_Cognee_Backend` (Python - The AI Brain & Memory System)
* **What it is:** A FastAPI server integrated with Cognee (Graph Database) and LiteLLM (AI Routing).
* **Responsibilities:**
  * Acts as the memory layer. It receives structured market data payloads from the C# engine.
  * **Memory Injection:** Uses Gemini and Embedding models to extract entities (e.g., "bShape Profile", "Monday", "TP Hit") and maps their relationships into a searchable Knowledge Graph.
  * **Pre-Trade Analysis:** When the C# engine asks for permission to trade, this backend searches the graph for similar past setups, feeds them into the Gemini LLM, and returns a strict probabilistic decision (e.g., "85% Win Probability").

---

## 🔄 Overall Data Flow & Lifecycle

The system operates in a continuous, two-phase loop during the trading day:

### Phase 1: Pre-Trade Active Analysis (The "Search & Suggest" Phase)
1. **Trigger:** At a specific time (e.g., 10:00 AM EST), the C# engine determines the Initial Balance has formed and identifies a valid technical setup (e.g., price is at the Point of Control).
2. **Request:** C# pauses execution and sends a `Payload 1` JSON to the Python `/analyze` endpoint, detailing the current market structure.
3. **Brain Evaluation:** The Python server embeds the search query, traverses the graph for historical matches, and asks Gemini to evaluate the setup. 
4. **Decision:** Python returns a `win_probability` to C#.
5. **Execution:** If the probability is high (e.g., >= 40%), C# places the trade. If it is low, the trade is vetoed, saving the user from a likely loss.

### Phase 2: Post-Trade Consolidated Recap (The "Memory" Phase)
1. **Trigger:** The open trade concludes (hits Take Profit, hits Stop Loss, or ends the day).
2. **Write-Ahead Log:** C# logs the result locally to disk to ensure zero data loss.
3. **Memory Update:** C# sends a massive `Payload 2` JSON to the Python `/memory` endpoint containing the exact setup and the final outcome.
4. **Asynchronous Injection:** To avoid lagging the trading engine or hitting AI rate limits, Python instantly returns a `200 OK` and places the payload in an asynchronous queue.
5. **Graph Cognification:** A background worker slowly processes the queue, using Gemini and Embeddings to map the new trade outcome into the graph. The Brain has now permanently learned from this trade.

---

## 👤 Use Cases

* **The Safe Automated Trader:** A retail trader runs the system live. The C# engine acts as the fast "hands" while the Python engine acts as the cautious "Brain." The user benefits from institutional-grade logic without needing to constantly monitor charts, trusting the AI to veto bad days.
* **The Quant Researcher:** A data scientist uses the Cognee graph database post-market to query complex relationships. For example, they can visualize a graph of "How often do bShape profiles on Mondays result in a Take Profit when the system bias is BUY?" to discover new alpha.

---

## 🛡️ Edge Cases Handled

1. **AI API Rate Limits / Timeout Protection:**
   * LLM API calls can be slow or hit rate limits. The Python server uses an asynchronous pacing worker loop (queueing system) for inserting memory. 
   * Live pre-trade queries have a strict 3.5-second timeout. If the AI is down or slow, the C# engine handles the timeout gracefully and defaults to its base mathematical rules or aborts safely.
2. **Network Failures & Data Loss:**
   * If the Python backend crashes, the C# engine writes all trade recaps to local CSV and `.txt` Write-Ahead Logs before transmitting. No historical trade data is ever lost.
3. **Unrecognized Market Contexts:**
   * What if the market forms a completely unprecedented structure that the Graph Database has never seen? The system falls back on the LLM's base reasoning capabilities to evaluate the raw structural state from first principles, returning a neutral probability until real memory is established.
