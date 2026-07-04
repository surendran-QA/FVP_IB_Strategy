# FVP IB Strategy - Trading Engine Testing Guide

This repository contains the C# algorithmic trading engine designed for the Quantower platform. It strictly enforces the Initial Balance (IB) trading logic and automatically sends formatted payloads to the Python AI Backend.

## Compilation and Deployment

To run this strategy inside Quantower, it must be compiled into a single `.dll` file.

1. Open this repository in Visual Studio or JetBrains Rider.
2. Build the project (Release mode is recommended).
3. Copy the resulting `FVP_IB_Strategy.dll` file.
4. Navigate to your Quantower installation folder and paste the `.dll` into `Settings\Scripts\Strategies\`.

> [!WARNING]
> **Strict Deployment Rule**
> NEVER place `.cs` (C# source files) inside the Quantower `Strategies` or `Indicators` folders. Ensure the folders are completely clean of everything except the `.dll` files to avoid internal compilation conflicts.

## Testing the System (Market Replay Mode)

You can evaluate the system using Quantower's **Market Replay Mode** to fast-forward historical data. You have two options depending on how deep you want to test.

### Option A: The Lightweight Approach (Using the Indicator) - **RECOMMENDED FOR HACKATHON**
If you want to train the AI and verify the API connection quickly without dealing with Strategy execution complexities, use the visual indicator. The Indicator has a built-in mathematical trade simulator that perfectly replicates trade entries/exits without firing real broker orders!

1. **Start the AI Backend:** Ensure the Python `START_COGNEE_BACKEND.bat` is running in the other repository.
2. **Open Quantower** and launch a chart (e.g., NQ futures).
3. **Open the Market Replay Panel**, select your historical dates, and click Play.
4. **Attach the Indicator:** Right-click the chart -> Indicators -> Add `FVP_IB_Visualizer`. 
5. **Configuration:** Ensure **Enable Webhook** is checked `true` in the indicator settings.
6. **Result:** As the replay fast-forwards, the indicator will instantly fire Phase 1 setups (`/analyze`) to the Python backend. Furthermore, as its internal engine simulates the trade, it will also fire the final Phase 2 Outcome payloads (`/memory`) exactly like the live strategy would! This fully trains the AI on both setups and win/loss outcomes safely.

### Option B: The Full Live Broker Approach (Using the Strategy)
If you want to fully train the AI's memory graph with trade outcomes (Entry, TP, SL, Exit Reasons):

1. Follow steps 1-3 above.
2. **Attach the Strategy:** Open the Strategy Runner panel, select `FVP_IB_Strategy`, and attach it to your chart. Ensure **Enable Webhook** is `true`.
6. **Start Replay:** Click Play on the Market Replay panel. 

> [!NOTE]
> **Automated Execution**
> You do NOT need to execute trades manually! As the replay fast-forwards through the historical data, the strategy will automatically generate limit orders, execute them at structural nodes, and fire HTTP requests to the AI backend exactly as it would in real-time. This is the fastest way to "train" the AI and verify the connection.

## Verifying the AI Connection

While the replay is running, you can monitor the following file to see exactly what the C# engine is sending to the Python brain:
`C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\AI_Global_Events.log`
