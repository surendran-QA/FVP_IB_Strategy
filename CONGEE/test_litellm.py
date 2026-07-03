import os
from litellm import completion
from dotenv import load_dotenv

load_dotenv()

key = os.getenv("LLM_API_KEY")
print(f"Testing API Key starting with: {key[:10] if key else 'None'}")

models_to_test = [
    "gemini/gemini-3.5-flash",
    "gemini/gemini-3.0-flash",
    "gemini/gemini-2.5-pro",
    "gemini/gemini-2.0-flash",
    "gemini/gemini-2.0-pro"
]

for model in models_to_test:
    print(f"\n--- Testing model: {model} ---")
    try:
        response = completion(
            model=model,
            messages=[{"role": "user", "content": "Hello"}],
            api_key=key
        )
        print("Success!")
        print(response.choices[0].message.content)
    except Exception as e:
        print(f"Failed: {type(e).__name__} - {str(e)}")
