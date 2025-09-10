# maintest.py
import os
import uuid
import logging
from datetime import datetime
from functools import lru_cache
import pandas as pd
import requests
from flask import Flask, request, jsonify
from flask_cors import CORS
from dotenv import load_dotenv

load_dotenv()

# -------------------------------------------------------------------
# Logging
# -------------------------------------------------------------------
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s :: %(message)s"
)
log = logging.getLogger("meal-backend")

# -------------------------------------------------------------------
# API Keys (ensure these are set in .env)
# -------------------------------------------------------------------
API_NINJAS_KEY = os.getenv("API_NINJAS_KEY")
USDA_API_KEY = os.getenv("USDA_API_KEY")
EDAMAM_APP_ID = os.getenv("EDAMAM_APP_ID")
EDAMAM_APP_KEY = os.getenv("EDAMAM_APP_KEY")

if not API_NINJAS_KEY:
    log.warning("API_NINJAS_KEY not set (API Ninjas fallback disabled)")
if not USDA_API_KEY:
    log.warning("USDA_API_KEY not set (USDA fallback disabled)")
if not (EDAMAM_APP_ID and EDAMAM_APP_KEY) or "your_edamam_app_id_here" in (EDAMAM_APP_ID or ""):
    log.warning("Edamam credentials missing or placeholder (Edamam fallback disabled)")

# -------------------------------------------------------------------
# Flask
# -------------------------------------------------------------------
app = Flask(__name__)
CORS(app)

DATA_FILE = os.getenv("FEL_CSV", "fel_data.csv")
DEFAULT_PORT = int(os.getenv("PORT", "5000"))

# -------------------------------------------------------------------
# Dataset
# -------------------------------------------------------------------
def load_dataset(path: str):
    if not os.path.exists(path):
        log.error("Dataset file not found: %s", path)
        return None
    try:
        df = pd.read_csv(path)
        if "Food" not in df.columns:
            log.error("Dataset missing Food column")
            return None
        df["Food"] = df["Food"].astype(str).str.strip().str.lower()
        log.info("Loaded FEL dataset rows=%d", len(df))
        return df
    except Exception as e:
        log.exception("Failed to load dataset: %s", e)
        return None

fel = load_dataset(DATA_FILE)

# -------------------------------------------------------------------
# Helpers
# -------------------------------------------------------------------
def safe_float(v, d=0.0):
    try:
        return float(v)
    except:
        return d

def bmi_category(bmi: float):
    if bmi < 18.5:
        return "Underweight", [5]
    if bmi < 24.9:
        return "Normal", [6]
    if bmi < 29.9:
        return "Overweight", [7]
    return "Obese", [8]

def scale_row(row, grams=100.0):
    portion = safe_float(row.get("Portion(g)"), 100) or 100
    factor = grams / portion
    return {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodCategoryId": row.get("Category"),
        "FoodId": str(row.get("Food")).lower(),
        "Calories": round(safe_float(row.get("Energy(kcal)")) * factor, 2),
        "Protein": round(safe_float(row.get("PRO(g)")) * factor, 2),
        "Fat": round(safe_float(row.get("FAT(g)")) * factor, 2),
        "Carbs": round(safe_float(row.get("CHO(g)")) * factor, 2),
        "UserId": "Unknown",
        "FoodGramAmount": grams,
        "Source": "FEL"
    }

# -------------------------------------------------------------------
# External API Fallbacks (caching)
# -------------------------------------------------------------------
external_cache = {}  # key: (food, grams_rounded) -> dict

def cache_key(food_name: str, grams: float):
    return (food_name.lower().strip(), int(round(grams)))

def cached_return(food_name: str, grams: float):
    return external_cache.get(cache_key(food_name, grams))

def store_cache(food_name: str, grams: float, value: dict):
    external_cache[cache_key(food_name, grams)] = value

def normalize_external_item(food_name: str, grams: float, src: str, calories=0, protein=0, fat=0, carbs=0):
    return {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodCategoryId": src,
        "FoodId": food_name.lower(),
        "Calories": round(calories, 2),
        "Protein": round(protein, 2),
        "Fat": round(fat, 2),
        "Carbs": round(carbs, 2),
        "UserId": "Unknown",
        "FoodGramAmount": grams,
        "Source": src
    }

def lookup_usda(food_name: str, grams: float):
    if not USDA_API_KEY:
        return None
    try:
        params = {"query": food_name, "api_key": USDA_API_KEY, "pageSize": 1}
        r = requests.get("https://api.nal.usda.gov/fdc/v1/foods/search", params=params, timeout=7)
        if r.status_code != 200:
            return None
        foods = r.json().get("foods", [])
        if not foods:
            return None
        f = foods[0]
        nutrients = {n.get("nutrientName"): n.get("value") for n in f.get("foodNutrients", []) if "nutrientName" in n}
        # USDA returns per 100g
        base = 100.0
        factor = grams / base
        calories = safe_float(nutrients.get("Energy"), 0) * factor
        protein = safe_float(nutrients.get("Protein"), 0) * factor
        fat = safe_float(nutrients.get("Total lipid (fat)"), 0) * factor
        carbs = safe_float(nutrients.get("Carbohydrate, by difference"), 0) * factor
        return normalize_external_item(food_name, grams, "USDA", calories, protein, fat, carbs)
    except Exception as e:
        log.warning("USDA lookup failed for %s: %s", food_name, e)
        return None

def lookup_edamam(food_name: str, grams: float):
    if not (EDAMAM_APP_ID and EDAMAM_APP_KEY) or "your_edamam_app_id_here" in EDAMAM_APP_ID:
        return None
    try:
        params = {
            "app_id": EDAMAM_APP_ID,
            "app_key": EDAMAM_APP_KEY,
            "ingr": f"100g {food_name}"
        }
        r = requests.get("https://api.edamam.com/api/nutrition-data", params=params, timeout=7)
        if r.status_code != 200:
            return None
        data = r.json()
        # Edamam returns per 100g
        base = 100.0
        factor = grams / base
        calories = safe_float(data.get("calories"), 0) * factor
        tn = data.get("totalNutrients", {})
        protein = safe_float(tn.get("PROCNT", {}).get("quantity"), 0) * factor
        fat = safe_float(tn.get("FAT", {}).get("quantity"), 0) * factor
        carbs = safe_float(tn.get("CHOCDF", {}).get("quantity"), 0) * factor
        return normalize_external_item(food_name, grams, "Edamam", calories, protein, fat, carbs)
    except Exception as e:
        log.warning("Edamam lookup failed for %s: %s", food_name, e)
        return None

def lookup_api_ninjas(food_name: str, grams: float):
    if not API_NINJAS_KEY:
        return None
    try:
        r = requests.get(
            "https://api.api-ninjas.com/v1/nutrition",
            params={"query": f"100 g {food_name}"},
            headers={"X-Api-Key": API_NINJAS_KEY},
            timeout=7
        )
        if r.status_code != 200:
            return None
        arr = r.json()
        if not arr:
            return None
        d = arr[0]
        # API Ninjas returns per 100g
        base = 100.0
        factor = grams / base
        calories = safe_float(d.get("calories"), 0) * factor
        protein = safe_float(d.get("protein_g"), 0) * factor
        fat = safe_float(d.get("fat_total_g"), 0) * factor
        carbs = safe_float(d.get("carbohydrates_total_g"), 0) * factor
        return normalize_external_item(food_name, grams, "API_Ninjas", calories, protein, fat, carbs)
    except Exception as e:
        log.warning("API Ninjas lookup failed for %s: %s", food_name, e)
        return None

def external_fallback(food_name: str, grams: float):
    cached = cached_return(food_name, grams)
    if cached:
        return cached
    for fn in (lookup_usda, lookup_edamam, lookup_api_ninjas):
        item = fn(food_name, grams)
        if item:
            store_cache(food_name, grams, item)
            return item
    empty = normalize_external_item(food_name, grams, "None", 0, 0, 0, 0)
    empty["Note"] = "No external data"
    store_cache(food_name, grams, empty)
    return empty

# -------------------------------------------------------------------
# Health
# -------------------------------------------------------------------
@app.route("/health")
def health():
    return jsonify({
        "status": "ok",
        "dataset_loaded": fel is not None,
        "rows": int(len(fel)) if fel is not None else 0,
        "external": {
            "usda": bool(USDA_API_KEY),
            "edamam": bool(EDAMAM_APP_ID and EDAMAM_APP_KEY and "your_edamam" not in EDAMAM_APP_ID),
            "api_ninjas": bool(API_NINJAS_KEY)
        },
        "time": datetime.utcnow().isoformat()
    })

# -------------------------------------------------------------------
# Meal Plan
# -------------------------------------------------------------------
@app.route("/get_food_recommendations", methods=["POST"])
def get_food_recommendations():
    if fel is None or fel.empty:
        return jsonify({"error": "Dataset not available"}), 500

    data = request.get_json(silent=True) or {}
    try:
        weight = float(data.get("weight"))
        height_cm = float(data.get("height_cm"))
    except:
        return jsonify({"error": "Invalid or missing weight/height_cm"}), 400

    max_results = int(data.get("max_results", 28))
    if max_results <= 0:
        max_results = 28
    if max_results % 4 != 0:
        max_results += (4 - max_results % 4)

    bmi = round(weight / ((height_cm / 100) ** 2), 2)
    category, cat_ids = bmi_category(bmi)
    pool = fel[fel["Category"].isin(cat_ids)]
    if pool.empty:
        pool = fel
        log.warning("No BMI category rows; broadened pool.")

    foods = []
    meal_cycle = ["breakfast", "lunch", "dinner", "snack"]

    while len(foods) < max_results:
        batch = pool.sample(
            n=min(len(pool), max_results - len(foods)),
            replace=len(pool) < (max_results - len(foods))
        )
        for _, row in batch.iterrows():
            idx = len(foods)
            item = scale_row(row)
            item["MealType"] = meal_cycle[idx % 4]
            item["DayIndex"] = idx // 4
            foods.append(item)
        if len(foods) >= max_results:
            break

    return jsonify({
        "prompt": "All nutritional calculations use the Philippine FEL database and standards whenever possible. Only use international sources if no FEL entry exists.",
        "bmi": bmi,
        "category": category,
        "foods": foods,
        "meta": {
            "requested": max_results,
            "returned": len(foods),
            "days": len(foods) // 4
        }
    })

# -------------------------------------------------------------------
# Nutritional Info
# -------------------------------------------------------------------
@app.route("/get_nutritional_info", methods=["POST"])
def get_nutritional_info():
    data = request.get_json(silent=True) or {}
    items = data.get("items", [])
    include_sources = request.args.get("include_sources") == "1"

    results = []
    alerts = []

    for raw in items:
        name = str(raw.get("foodName", "unknown")).strip().lower()
        grams = safe_float(raw.get("grams"), 100)

        match_row = None
        if fel is not None and not fel.empty:
            subset = fel[fel["Food"].str.contains(name, na=False)]
            if not subset.empty:
                match_row = subset.iloc[0]

        if match_row is not None:
            item = scale_row(match_row, grams)
            item["Source"] = "FEL"
            item["Note"] = "Philippines FEL standard used"
        else:
            item = external_fallback(name, grams)
            item["Note"] = "External fallback used (not PH FEL standard)"

        if item["Calories"] > 800:
            alerts.append(f"High calories: {item['FoodId']} ({item['Calories']})")
        if item["Fat"] > 30:
            alerts.append(f"High fat: {item['FoodId']} ({item['Fat']})")

        results.append(item)

    response = {
        "foods": results,
        "body_goal_note": "Philippines FEL standard used where available. External fallbacks applied only if no FEL match."
    }
    if alerts:
        response["realtime_alert"] = True
        response["alert_reason"] = alerts
    if include_sources:
        response["sources_used"] = {
            "USDA": bool(USDA_API_KEY),
            "API_Ninjas": bool(API_NINJAS_KEY),
            "Edamam": bool(EDAMAM_APP_ID and EDAMAM_APP_KEY and 'your_edamam' not in EDAMAM_APP_ID)
        }

    return jsonify(response)

# -------------------------------------------------------------------
# Errors
# -------------------------------------------------------------------
@app.errorhandler(404)
def not_found(_):
    return jsonify({"error": "Not found"}), 404

@app.errorhandler(500)
def internal_err(e):
    log.exception("Unhandled error: %s", e)
    return jsonify({"error": "Internal server error"}), 500

# -------------------------------------------------------------------
# Entry
# -------------------------------------------------------------------
if __name__ == "__main__":
    log.info("Starting server on 0.0.0.0:%d (dataset_loaded=%s)", DEFAULT_PORT, fel is not None)
    app.run(host="0.0.0.0", port=DEFAULT_PORT, debug=True)
