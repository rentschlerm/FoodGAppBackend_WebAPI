import google.generativeai as genai

genai.configure(api_key="AIzaSyCnrTpxE-n-Z1TJ2aWB7KUTTsUsa7Z4hGE")

model = genai.GenerativeModel("gemini-1.5-pro")

try:
    response = model.generate_content("Say hello.")
    print(response.text)
except Exception as e:
    print("API error:", e)
