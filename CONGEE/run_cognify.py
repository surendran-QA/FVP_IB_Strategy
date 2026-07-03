import asyncio
import cognee
from dotenv import load_dotenv

load_dotenv()

async def run_batch():
    print("="*50)
    print("Initiating Batch Knowledge Graph Extraction...")
    print("="*50)
    
    print("Connecting to Cognee and calling Gemini API...")
    try:
        await cognee.cognify()
        print("Success! All pending memories have been cognified and integrated into the Knowledge Graph.")
    except Exception as e:
        print(f"Error during cognification: {e}")

if __name__ == "__main__":
    asyncio.run(run_batch())
