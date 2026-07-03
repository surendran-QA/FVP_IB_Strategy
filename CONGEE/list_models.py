import os
from google import genai
from dotenv import load_dotenv

load_dotenv()

key = os.getenv("LLM_API_KEY")

try:
    client = genai.Client(api_key=key)
    print("Listing available models...")
    for model in client.models.list():
        if "embed" in model.name.lower():
            print(f"Found embedding model: {model.name}")
except Exception as e:
    print(f"Failed to list models: {type(e).__name__} - {str(e)}")
