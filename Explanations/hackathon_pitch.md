# Hackathon Pitch: Ninaivaatral Quant (FVP IB Strategy)

## 1. The Hook (The Problem)
In the world of algorithmic trading, we face a massive dilemma:
* **Human traders** have great intuition and "memory" of market context, but they are plagued by emotions, fatigue, and poor risk management.
* **Algorithmic bots** execute flawlessly without emotion, but they are rigid. A standard moving-average bot will blindly buy into a brick wall of resistance because it lacks contextual memory and cannot adapt to changing market conditions.

**What if we could combine the flawless execution of a machine with the contextual memory of an AI?**

## 2. The Solution
Introducing **Ninaivaatral Quant** (FVP IB Strategy). 
It is a hybrid quantitative trading system that bridges a high-performance C# execution engine with an advanced LLM-powered Knowledge Graph memory backend. 

Instead of relying on lagging indicators, we use an AI brain to remember exactly how complex market structures (like Volume Profiles and Initial Balance shapes) have played out in the past, allowing the system to adapt in real-time.

## 3. The Tech Stack (The Flex)
We built a deeply decoupled, event-driven architecture to marry low-latency trading with heavy AI compute:

* **The Edge (Execution):** C# .NET compiling directly into the Quantower Trading Platform. Handles microsecond-level tick data and structural math (Volume Profile generation).
* **The Bridge:** Python FastAPI acting as an asynchronous webhook. It utilizes a Write-Ahead Log (WAL) and an `asyncio` paced queue to prevent data loss and protect LLM rate limits without blocking the trading UI.
* **The Brain (Cognee + LLMs):** 
  * **Gemini LLM** to parse complex JSON market payloads and extract structural entities.
  * **LanceDB (Vector Store)** for blazing-fast semantic similarity searches (e.g., "Find me a day with a similar volume structure").
  * **Kuzu (Graph Database)** to map the extracted entities into a Knowledge Graph (e.g., `[IB High] --ACTED_AS--> [Resistance]`).
  * **SQLite** for relational metadata management.

## 4. How It Works (The Demo Flow)
1. **Detection:** The C# engine watches the first 30 minutes of the market (The Initial Balance). It detects a specific structural pattern (e.g., a "b-shape" profile) and formats this into a JSON payload.
2. **Memory Retrieval:** Before trading, it pings the Python AI server. The AI queries LanceDB and Kuzu to find historical matches of this exact structural setup.
3. **Reasoning:** The LLM analyzes the historical graph data and returns a probabilistic "AI Score" (e.g., "This setup has an 85% win probability based on the last 10 similar graph structures").
4. **Execution:** The C# engine receives the score and dynamically executes the trade, hiding its stop-loss behind heavy volume nodes.

## 5. Why It Wins
We aren't just calling a ChatGPT wrapper to ask for trading advice. We are actively converting raw, live market microstructure data into a **Queryable Knowledge Graph**. We are solving the biggest problem in automated trading—regime change—by giving an algorithm the ability to remember, contextualize, and adapt.
