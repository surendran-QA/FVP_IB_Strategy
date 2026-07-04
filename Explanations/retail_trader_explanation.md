# FVP IB Strategy: Retail Trader Overview

## What is this strategy?
The `FVP_IB_Strategy` is an automated trading tool that runs on the Quantower platform. It is designed to find high-probability trades by looking at where the "smart money" is trading, rather than relying on standard lagging indicators like RSI or MACD.

## How it works (In plain English)

### 1. The Initial Balance (IB)
The strategy waits and watches the first 30 minutes of the trading day. This window is called the Initial Balance. Big institutional players usually show their hand during this time, creating specific shapes on the chart (like a "P" shape or a "b" shape). The strategy reads these shapes to determine if the market is trending, ranging, or reversing.

### 2. The Volume Profile
Instead of just looking at price, the strategy looks at **Volume** at specific price levels. It identifies "Value Areas" (where 70% of the trading happened) and "High Volume Nodes" (massive clusters of buying/selling). 
- **Entries:** It looks to enter trades when price pulls back to these important volume levels.
- **Stop Losses (Protection):** It doesn't use random 10-tick stop losses. It hides your stop loss safely behind heavy walls of volume, making it much harder for the market to randomly stop you out.

### 3. The AI Brain (The Game Changer)
Here is what makes this tool completely different from a standard trading bot. 

Every time a trade setup happens, the system takes a data "screenshot" and sends it to an AI memory bank. Over time, the AI learns. 
When a new trade sets up tomorrow, the system asks the AI: *"Hey, have we seen this exact shape and volume structure before? Did it win or lose?"* 

The AI instantly searches its memory of past trades and tells the strategy whether this is a high-probability "A+" setup, or if it's a trap that usually fails. This helps the bot automatically filter out bad days and avoid getting chopped up, mimicking the "gut feeling" of a seasoned veteran trader, but backed by hard data.
