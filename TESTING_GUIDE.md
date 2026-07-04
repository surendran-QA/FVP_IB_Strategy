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
> NEVER place `.cs` (C# source files) inside the Quantower `Strategies` folder. Ensure the folder is completely clean of everything except the `.dll` files to avoid internal compilation conflicts.

## Testing the Strategy (Market Replay Mode)

For hackathon judges or rapid testing, you do not need to wait for a live market session. You can test the entire system, including the AI integration, instantly using Quantower's **Market Replay Mode**.

1. **Start the AI Backend:** Ensure the Python `START_COGNEE_BACKEND.bat` is running in the other repository.
2. **Open Quantower** and launch a chart (e.g., NQ futures).
3. **Open the Market Replay Panel** and select your desired historical dates.
4. **Attach the Strategy:** Open the Strategy Runner panel, select `FVP_IB_Strategy`, and attach it to your chart.
5. **Configuration:**
   - Set the `IB Duration (Minutes)` (default is 30).
   - Check the **Enable Webhook** parameter to `true`. This is required to send data to the AI.
6. **Start Replay:** Click Play on the Market Replay panel. 

> [!NOTE]
> **Automated Execution**
> You do NOT need to execute trades manually! As the replay fast-forwards through the historical data, the strategy will automatically generate limit orders, execute them at structural nodes, and fire HTTP requests to the AI backend exactly as it would in real-time. This is the fastest way to "train" the AI and verify the connection.

## Verifying the AI Connection

While the replay is running, you can monitor the following file to see exactly what the C# engine is sending to the Python brain:
`C:\AMP Quantower\Settings\Scripts\Strategies\FVP_IB_Strategy\AI_Global_Events.log`
