# Step-by-Step Flow with Sample Data

To deeply understand how the system works, let's walk through a hypothetical trading day step-by-step, looking at the exact data being passed around.

## The Scenario
- **Asset:** Nasdaq 100 Futures (NQ)
- **Time:** 10:00 AM (Initial Balance just closed)
- **Market State:** The market dropped early and consolidated at the bottom, forming a "b-shape" Volume Profile.

---

## Phase 1: The Pre-Trade Query (`/analyze`)

### Step 1: Quantower builds the Payload
At exactly 10:00 AM, the C# strategy detects the "b-shape". Before it places a trade, it needs the AI's opinion. It builds this JSON payload:

```json
{
  "endpoint": "analyze",
  "payload": {
    "trade_id": "NQ_20260703_1000",
    "temporal_data": {
      "session": "Morning Session",
      "day_of_week": "Tuesday",
      "time": "10:00 AM"
    },
    "asset_identity": {
      "ticker": "NQ",
      "asset_class": "Futures",
      "timeframe": "30-min"
    },
    "microstructure_state": {
      "ib_shape": "b-Shape",
      "ib_high": 19600.00,
      "ib_low": 19450.00,
      "value_area_high": 19550.00,
      "value_area_low": 19500.00,
      "point_of_control": 19525.00,
      "key_hvn": 19510.00
    }
  }
}
```

### Step 2: Sending to Python
Quantower fires an HTTP POST request to `http://localhost:8000/analyze`.
*Note: Because Quantower needs an answer back immediately to decide whether to trade, this specific endpoint processes the data right then and there. It takes about 2-3 seconds.*

### Step 3: Cognee Search & Gemini Evaluation
The Python server receives the JSON. Here is the exact chain of logic:
1. **The Search:** Cognee (the Python library) takes your current payload and searches the **LanceDB** vector database and **Kuzu** graph database to find similar historical setups.
2. **The Context Hand-off:** Cognee retrieves the historical results (e.g., the last 5 times this exact structure happened on NQ) and packages them together with your live payload.
3. **The LLM Evaluation:** Cognee sends this combined "context package" over the internet to the **Gemini API**. Gemini evaluates the history and realizes: *"Wait, in 4 out of the 5 past times we saw a b-shape with POC at 19525 and HVN at 19510, price successfully bounced off the HVN."*

### Step 4: The AI Response
Python sends this JSON response back to Quantower:
```json
{
  "status": "success",
  "ai_score": "85",
  "win_probability": "75%",
  "narrative": "Similar b-shape setups showed strong bounces off the HVN."
}
```

---

## The Active Trade (No Data Sent)
Quantower receives the 85% score. It decides to execute!
It places a Limit Buy at 19510 (the HVN). The trade triggers. 
For the next 2 hours, the trade is running. **Zero data is sent to the AI.** We do not spam the Python server.

---

## Phase 2: Post-Trade Ingestion (`/memory`)

### Step 5: The Trade Closes
At 12:15 PM, the trade hits Take Profit (TP) for +$500. 

### Step 6: Building the Final Memory Payload
Quantower now needs to tell the AI how the story ended, so the AI can learn from it. It combines the Phase 1 setup with the final outcome into a massive new JSON payload:

```json
{
  "endpoint": "memory",
  "payload": "[SESSION ID: NQ_2026-07-04]\n[MARKET CONTEXT NODE]\nEvent Tag: Take Profit\nDay of Week: Tuesday\nAsset: NQ\nTimestamp: 2026-07-04 10:00:00\n\n[STRUCTURAL STATE NODE]\nProfile Shape: bShape\nTotal Session Volume: 154200\nSession Extremes: IB_High 19600.00 | IB_Low 19450.00\nValue Area: VAH 19550.00 (66.7%) | POC 19525.00 (50.0%) | VAL 19500.00 (33.3%)\nMicrostructure: HVN1 19510.00 | HVN2 - | LVN_Gap -\n\n[EXECUTION PLAN NODE]\nSystem Bias: LONG\nOrder Setup: Entry 19510.00 | Take Profit 19560.00 | Stop Loss 19490.00\n\n[OUTCOME NODE]\nResult: Win\nExit Reason: Take Profit\n\n[RELATIONAL SUMMARY]\nAt 10:00:00 on Tuesday, 2026-07-04, NQ established its Initial Balance, resolving into a bShape distribution with a total volume of 154200. \nPrimary institutional value is anchored at the POC of 19525.00 (50.0% up from low), contained within the VAH (19550.00) and VAL (19500.00) boundaries. Session liquidity extremes are marked at a High of 19600.00 and a Low of 19450.00. \nMicrostructure analysis dictates primary liquidity sitting at HVN1 (19510.00) and secondary liquidity at HVN2 (-), divided by a low-volume liquidity void at LVN (-). \nBased on this structural state, the algorithm confirms a LONG execution logic. The strategic plan dictates an Entry at 19510.00, structural invalidation (Stop Loss) at 19490.00, and a liquidity target (Take Profit) at 19560.00.\nThe outcome of the setup was a Win due to Take Profit."
}
```

### Step 7: The Asynchronous Dump (Write-Ahead Log)
Quantower sends this to `http://localhost:8000/memory`. 
Because Quantower doesn't need an answer back, the Python server just says *"Got it!"* instantly and saves the JSON to a text file on your hard drive (The Write-Ahead Log). Quantower goes right back to charting with zero freezing.

### Step 8: The Background Worker (The Shock Absorber)
A few seconds later, the Python Background Worker wakes up. 
1. It reads the JSON file.
2. It sends it to Gemini to extract the fact that "HVN at 19510 acted as Support".
3. It saves this new fact permanently into the Kuzu Knowledge Graph.
4. It deletes the JSON file from the hard drive.
5. **The Delay:** The worker goes to sleep for 20 seconds to make sure Gemini doesn't get mad at us for sending too many requests, before checking the queue for the next trade.
