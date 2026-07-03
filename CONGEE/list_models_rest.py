import os
import requests
from dotenv import load_dotenv

load_dotenv()

key = os.getenv("LLM_API_KEY")

url = f"https://generativelanguage.googleapis.com/v1beta/models?key={key}"

print("Fetching models via REST API...")
response = requests.get(url)

if response.status_code == 200:
    data = response.json()
    models = data.get("models", [])
    print(f"Total models available: {len(models)}")
    
    print("\n--- Embedding Models found ---")
    found_embed = False
    for model in models:
        name = model.get("name", "")
        if "embed" in name.lower():
            found_embed = True
            print(f"- {name}: {model.get('description', '')}")
            
    if not found_embed:
        print("No embedding models found for this key!")
        print("\nAll available models:")
        for model in models:
            print(f"- {model.get('name')}")
else:
    print(f"Failed to fetch models: {response.status_code}")
    print(response.text)
