import os
from litellm import embedding
from dotenv import load_dotenv

load_dotenv()

key = os.getenv("LLM_API_KEY")

try:
    print(f"\n--- Testing embedding model: gemini/gemini-embedding-2 ---")
    response = embedding(
        model="gemini/gemini-embedding-2",
        input=["Testing new embedding model"],
        api_key=key
    )
    print("Success! Dimensions:", len(response.data[0].embedding))
except Exception as e:
    print(f"Failed: {type(e).__name__} - {str(e)}")
