import cognee
import asyncio
import os
from dotenv import load_dotenv

# Load your .env file
load_dotenv()

async def main():
    print("Cleaning slate for fresh test...")
    # Reset data and system state to ensure a clean run
    await cognee.prune.prune_data()
    await cognee.prune.prune_system(metadata=True)
    
    print("Adding market event to memory...")
    # This simulates the text payload your C# strategy will eventually send
    market_event = """
    [Asset: NQ] [Prev Day Profile: P-Shape] [IB State: Breakout High] 
    [CVD Correlation: Bullish] [Trade Result: Target Hit]. 
    Institutional absorption was observed at the Value Area Low.
    """
    
    # 1. Add raw text to Cognee
    await cognee.add(market_event, dataset_name="nq_trades")
    
    print("Cognifying (Gemini is extracting the graph)...")
    # 2. Process data into the knowledge graph
    await cognee.cognify()
    
    print("Recalling from Memory...")
    # 3. Query the graph to see if it understood the relationships
    results = await cognee.search("Where was institutional absorption observed?")
    
    print("\n=== Search Results ===")
    for result in results:
        print(result)

if __name__ == '__main__':
    asyncio.run(main())
