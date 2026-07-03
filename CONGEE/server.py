import asyncio
from fastapi import FastAPI, Request
import uvicorn
import cognee
import os
import uuid
import json
import glob
from datetime import datetime
from dotenv import load_dotenv

def save_failed_payload(payload: str, endpoint: str):
    os.makedirs("FailedPayloads", exist_ok=True)
    filename = f"FailedPayloads/failed_{endpoint}_{datetime.now().strftime('%Y%m%d_%H%M%S')}_{uuid.uuid4().hex[:8]}.txt"
    with open(filename, "w") as f:
        f.write(payload)
    print(f"\n[!] ULTIMATE FALLBACK TRIGGERED:")
    print(f"[!] Payload safely written to {filename}\n")

load_dotenv()

# The fast in-memory queue
payload_queue = asyncio.Queue()
INGESTION_DIR = "IngestionQueue"
os.makedirs(INGESTION_DIR, exist_ok=True)

async def background_worker():
    print("[Background Worker] Started and listening for payloads...")
    while True:
        filepath = await payload_queue.get()
        print(f"[Background Worker] Processing {filepath}...")
        
        try:
            if not os.path.exists(filepath):
                print(f"[Background Worker] File {filepath} not found. Skipping.")
                payload_queue.task_done()
                continue
                
            with open(filepath, "r") as f:
                data = json.load(f)
                
            payload = data.get("payload")
            auto_cognify = data.get("auto_cognify", False)
            
            print("1. Injecting into Cognee Memory Engine...")
            await cognee.add(payload, dataset_name="nq_live_trades")
            
            if auto_cognify:
                print("2. Cognifying (Extracting Knowledge Graph via Gemini)...")
                await cognee.cognify()
                print("3. Memory successfully embedded and cognified!")
            else:
                print("2. Skipping Cognify (Saved for manual batch processing).")
            
            # Success! Delete the persistent file
            os.remove(filepath)
            
        except Exception as e:
            print(f"[Background Worker] Error processing memory: {str(e)}")
            if 'payload' in locals() and payload:
                save_failed_payload(payload, "memory")
            # Delete from ingestion queue since it failed (and moved to failed queue)
            if os.path.exists(filepath):
                os.remove(filepath)
        
        payload_queue.task_done()
        
        # CRITICAL PACING: Wait 20 seconds before taking next item from queue
        print("[Background Worker] Pacing... Sleeping for 20 seconds to protect RPM limit.")
        await asyncio.sleep(20)

app = FastAPI(title="Ninaivaatral Quant - Cognee Integration")

@app.on_event("startup")
async def startup_event():
    # 1. Recover any payloads that were dropped during a server crash
    pending_files = glob.glob(f"{INGESTION_DIR}/*.json")
    for file in pending_files:
        await payload_queue.put(file)
    if pending_files:
        print(f"[Startup] Recovered {len(pending_files)} pending payloads from disk!")
        
    # 2. Start the background worker
    asyncio.create_task(background_worker())


@app.post("/memory")
async def add_memory(request: Request):
    """
    Receives JSON payload from the Quantower C# Indicator, 
    adds it to the graph, and immediately cognifies it.
    """
    try:
        data = await request.json()
        payload = data.get("payload")
        
        if not payload:
            return {"status": "error", "message": "No payload provided"}
            
        # Write-Ahead Log: Save to persistent disk immediately
        filename = f"{INGESTION_DIR}/payload_{datetime.now().strftime('%Y%m%d_%H%M%S')}_{uuid.uuid4().hex[:8]}.json"
        with open(filename, "w") as f:
            json.dump(data, f)
            
        # Put into the fast async queue
        await payload_queue.put(filename)
        
        return {"status": "success", "message": "Payload securely queued for processing."}
        
    except Exception as e:
        print(f"Error queueing memory: {str(e)}")
        return {"status": "error", "message": str(e)}

@app.post("/analyze")
async def analyze_setup(request: Request):
    """
    Receives Morning Setup JSON payload from Indicator at 10:00 AM, 
    adds it to the graph, cognifies, and returns an AI score.
    """
    try:
        data = await request.json()
        payload = data.get("payload")
        
        if not payload:
            return {"status": "error", "message": "No payload provided"}
            
        print("\n" + "="*50)
        print(f"[Ninaivaatral Quant] Received ACTIVE ANALYSIS Request:")
        print(payload)
        print("="*50 + "\n")
        
        print("1. Injecting Setup into Cognee...")
        await cognee.add(payload, dataset_name="nq_live_trades")
        
        print("2. Cognifying Setup for Active Analysis...")
        await cognee.cognify()
        
        print("3. Generating AI Score (Placeholder Logic)...")
        # TODO: Replace placeholder with actual Gemini Prompt/Search logic
        ai_score = "85"
        win_probability = "75%"
        
        print(f"-> Returning Score: {ai_score} | Probability: {win_probability}\n")
        
        return {
            "status": "success", 
            "ai_score": ai_score, 
            "win_probability": win_probability,
            "narrative": "Similar past setups showed strong expansion."
        }
        
    except Exception as e:
        print(f"Error analyzing setup: {str(e)}")
        if payload:
            save_failed_payload(payload, "analyze")
        return {"status": "error", "message": str(e)}

if __name__ == "__main__":
    print("Starting Cognee Memory Webhook on http://localhost:8000...")
    uvicorn.run(app, host="0.0.0.0", port=8000)
