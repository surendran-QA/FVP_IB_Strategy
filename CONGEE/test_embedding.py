import os
from litellm import embedding
from dotenv import load_dotenv

load_dotenv()

key = os.getenv("LLM_API_KEY")
print(f"Testing API Key starting with: {key[:10] if key else 'None'}")

models_to_test = [
    "gemini/text-embedding-004",
    "gemini/embedding-001"
]

for model in models_to_test:
    print(f"\n--- Testing embedding model: {model} ---")
    try:
        response = embedding(
            model=model,
            input=["Hello world"],
            api_key=key
        )
        print("Success! Dimensions:", len(response.data[0].embedding))
    except Exception as e:
        print(f"Failed: {type(e).__name__} - {str(e)}")
