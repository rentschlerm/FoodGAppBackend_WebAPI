import os
import pandas as pd
from flask import Flask, request, jsonify
from datetime import datetime
from dotenv import load_dotenv
import google.generativeai as genai
import uuid
import logging
import requests

# Configure logging
logging.basicConfig(level=logging.DEBUG, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

load_dotenv()
genai.configure(api_key=os.getenv("SECRET_KEY"))
API_KEY_NINJAS = os.getenv("API_NINJAS_KEY")
API_KEY_USDA = os.getenv("USDA_API_KEY")
API_KEY_EDAMAM_APP_ID = os.getenv("EDAMAM_APP_ID")
API_KEY_EDAMAM_APP_KEY = os.getenv("EDAMAM_APP_KEY")

app = Flask(__name__)

# Load FEL dataset
try:
    base_dir = os.path.dirname(os.path.abspath(__file__))
    fel_path = os.path.join(base_dir, "fel_data.csv")
    fel_data = pd.read_csv(fel_path)
    fel_data['Food'] = fel_data['Food'].str.strip().str.lower()
    logger.info("FEL dataset loaded successfully with %d entries: %s", len(fel_data), fel_data['Food'].tolist())
except FileNotFoundError:
    fel_data = None
    logger.error("fel_data.csv not found in the script directory: %s", fel_path)
except Exception as e:
    fel_data = None
    logger.error("Failed to load fel_data.csv: %s", str(e))

# BMI Calculation Utility
def calculate_bmi(weight, height_cm):
    try:
        height_m = height_cm / 100
        bmi = weight / (height_m ** 2)
        return round(bmi, 2)
    except Exception as e:
        logger.error("BMI calculation error: %s", str(e))
        return None

# External Nutrition Fallbacks
def lookup_api_ninjas(food_name, grams):
    try:
        if not API_KEY_NINJAS:
            return None
        url = "https://api.api-ninjas.com/v1/nutrition"
        resp = requests.get(url, params={"query": f"{grams} g {food_name}"}, headers={"X-Api-Key": API_KEY_NINJAS})
        if resp.status_code != 200:
            logger.warning("API Ninjas request failed: %s", resp.text)
            return None
        data = resp.json()
        if not data:
            return None
        item = data[0]
        return {
            "NutrientLogId": str(uuid.uuid4()),
            "FoodCategoryId": "API_Ninjas",
            "FoodId": food_name,
            "Calories": item.get("calories", 0),
            "Protein": item.get("protein_g", 0),
            "Fat": item.get("fat_total_g", 0),
            "Carbs": item.get("carbohydrates_total_g", 0),
            "UserId": "Unknown",
            "FoodGramAmount": grams
        }
    except Exception as e:
        logger.error("API Ninjas error: %s", str(e))
        return None

def lookup_usda(food_name):
    try:
        if not API_KEY_USDA:
            return None
        search_url = f"https://api.nal.usda.gov/fdc/v1/foods/search?query={food_name}&api_key={API_KEY_USDA}"
        search_response = requests.get(search_url)
        if search_response.status_code != 200:
            logger.warning("USDA search error: %s", search_response.text)
            return None
        results = search_response.json().get("foods", [])
        if not results:
            return None
        food = results[0]
        nutrients = {n['nutrientName']: n['value'] for n in food['foodNutrients']}
        return {
            "NutrientLogId": str(uuid.uuid4()),
            "FoodCategoryId": "USDA",
            "FoodId": food_name,
            "Calories": nutrients.get("Energy", 0),
            "Protein": nutrients.get("Protein", 0),
            "Fat": nutrients.get("Total lipid (fat)", 0),
            "Carbs": nutrients.get("Carbohydrate, by difference", 0),
            "UserId": "Unknown",
            "FoodGramAmount": 100
        }
    except Exception as e:
        logger.error("USDA API error: %s", str(e))
        return None

def lookup_edamam(food_name):
    try:
        if not API_KEY_EDAMAM_APP_ID or not API_KEY_EDAMAM_APP_KEY:
            return None
        url = "https://api.edamam.com/api/nutrition-data"
        params = {
            "app_id": API_KEY_EDAMAM_APP_ID,
            "app_key": API_KEY_EDAMAM_APP_KEY,
            "ingr": f"100g {food_name}"
        }
        response = requests.get(url, params=params)
        if response.status_code != 200:
            logger.warning("Edamam API error: %s", response.text)
            return None
        data = response.json()
        return {
            "NutrientLogId": str(uuid.uuid4()),
            "FoodCategoryId": "Edamam",
            "FoodId": food_name,
            "Calories": data.get("calories", 0),
            "Protein": data.get("totalNutrients", {}).get("PROCNT", {}).get("quantity", 0),
            "Fat": data.get("totalNutrients", {}).get("FAT", {}).get("quantity", 0),
            "Carbs": data.get("totalNutrients", {}).get("CHOCDF", {}).get("quantity", 0),
            "UserId": "Unknown",
            "FoodGramAmount": 100
        }
    except Exception as e:
        logger.error("Edamam API error: %s", str(e))
        return None

# Gemini Food Description
@app.route('/describe_image', methods=['POST'])
def api_describe_image():
    if 'file' not in request.files:
        return jsonify({"error": "No file part"}), 400

    file = request.files['file']
    file_path = os.path.join("uploads", file.filename)
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    file.save(file_path)

    try:
        uploaded_file = genai.upload_file(file_path, mime_type="image/jpeg")
        model = genai.GenerativeModel("gemini-1.5-flash")
        result = model.generate_content([
            uploaded_file,
            "\n\n",
            "Please identify the Filipino food shown in the image. If the image is not food or is fake food, respond with: No food is detected! Respond with the food name and its category in this format: Food Name - Category."
        ])
        return jsonify({"description": result.text})
    except Exception as e:
        return jsonify({"error": str(e)}), 500

# Nutritional Info Endpoint
@app.route('/get_nutritional_info', methods=['POST'])
def nutritional_info():
    data = request.get_json()
    try:
        items = data.get("items", [])
        food_items = []
        alert = False
        alert_reason = []

        for item in items:
            food_name = item.get("foodName", "Unknown").strip().lower()
            grams = float(item.get("grams", 100))
            row = fel_data[fel_data['Food'].str.contains(food_name, na=False)] if fel_data is not None else pd.DataFrame()

            if not row.empty:
                f = row.iloc[0]
                factor = grams / float(f["Portion(g)"])
                food_item = {
                    "NutrientLogId": str(uuid.uuid4()),
                    "FoodCategoryId": f["Category"],
                    "FoodId": f["Food"],
                    "Calories": round(float(f["Energy(kcal)"]) * factor, 2),
                    "Protein": round(float(f["PRO(g)"]) * factor, 2),
                    "Fat": round(float(f["FAT(g)"]) * factor, 2),
                    "Carbs": round(float(f["CHO(g)"]) * factor, 2),
                    "UserId": "Unknown",
                    "FoodGramAmount": grams,
                    "Source": "FEL"
                }
            else:
                logger.info("FEL miss for %s", food_name)
                food_item = lookup_usda(food_name) or lookup_edamam(food_name) or lookup_api_ninjas(food_name, grams)
                if not food_item:
                    logger.warning("No fallback data found for %s", food_name)
                    food_item = {
                        "NutrientLogId": str(uuid.uuid4()),
                        "FoodCategoryId": "Unknown",
                        "FoodId": food_name,
                        "Calories": 0,
                        "Protein": 0,
                        "Fat": 0,
                        "Carbs": 0,
                        "UserId": "Unknown",
                        "FoodGramAmount": grams,
                        "Note": "No data available."
                    }
                    alert_reason.append(f"Missing data for {food_name}")

            if food_item["Calories"] > 800:
                alert = True
                alert_reason.append(f"High calories: {food_item['Calories']}")
            if food_item["Fat"] > 30:
                alert = True
                alert_reason.append(f"High fat: {food_item['Fat']}")

            food_items.append(food_item)

        response = {"foods": food_items, "body_goal_note": "General nutritional values applied."}
        if alert:
            response["realtime_alert"] = True
            response["alert_reason"] = alert_reason

        return jsonify(response)
    except Exception as e:
        logger.error("Failed to process request: %s", str(e))
        return jsonify({"error": "Server error."}), 500

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, debug=True)
