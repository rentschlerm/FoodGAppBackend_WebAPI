import os
import uuid
import logging
from datetime import datetime
from functools import lru_cache
import re
import difflib

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
# API Keys
# -------------------------------------------------------------------
USDA_API_KEY = os.getenv("USDA_API_KEY")
API_NINJAS_KEY = os.getenv("API_NINJAS_KEY")

if not USDA_API_KEY:
    log.warning("USDA_API_KEY not set (USDA lookup disabled)")
if not API_NINJAS_KEY:
    log.warning("API_NINJAS_KEY not set (API Ninjas lookup disabled)")

# -------------------------------------------------------------------
# Flask
# -------------------------------------------------------------------
app = Flask(__name__)
CORS(app)

DATA_FILE = os.getenv("FEL_CSV", "fel_data.csv")
DEFAULT_PORT = int(os.getenv("PORT", "5000"))

# -------------------------------------------------------------------
# URLs for external APIs
# -------------------------------------------------------------------
USDA_URL = "https://api.nal.usda.gov/fdc/v1/foods/search"
NINJAS_URL = "https://api.api-ninjas.com/v1/nutrition"


# -------------------------------------------------------------------
# Helpers
# -------------------------------------------------------------------
def safe_float(v, d=0.0):
    try:
        return float(v)
    except Exception:
        return d


def calculate_atwater_kcal(protein_g: float, fat_g: float, carbs_g: float) -> float:
    """Atwater 4-4-9 formula for calorie calculation"""
    return round((safe_float(protein_g) * 4.0) + (safe_float(carbs_g) * 4.0) + (safe_float(fat_g) * 9.0), 2)


def normalize_text(s: str) -> str:
    if s is None:
        return ""
    s = str(s).lower()
    s = re.sub(r"[^\w\s]", " ", s)  # Remove punctuation
    s = re.sub(r"\s+", " ", s).strip()
    return s


# -------------------------------------------------------------------
# Load FEL dataset (normalize columns)
# -------------------------------------------------------------------
@lru_cache(maxsize=1)
def load_dataset(path: str):
    if not os.path.exists(path):
        log.error("Dataset file not found: %s", path)
        return pd.DataFrame()
    try:
        df = pd.read_csv(path)
        cols = {c.lower().strip(): c for c in df.columns}

        # Normalize Food column
        food_col = cols.get("food") or cols.get("foodid") or df.columns[0]
        df["Food_raw"] = df[food_col].astype(str)
        df["Food"] = df["Food_raw"].apply(normalize_text)

        # Portion
        portion_col = None
        for cand in ("Portion(g)", "Portion_g", "portion(g)", "portion_g", "portion"):
            if cand in df.columns:
                portion_col = cand
                break
        df["Portion(g)"] = df[portion_col].apply(lambda v: safe_float(v, 100)) if portion_col else 100.0

        # Nutrient columns
        def find_col(cands):
            for c in cands:
                if c in df.columns:
                    return c
            return None

        cho_col = find_col(["CHO(g)", "CHO_g", "CHO", "cho(g)", "carbs", "carbohydrate", "carbohydrates"])
        pro_col = find_col(["PRO(g)", "PRO_g", "PRO", "pro(g)", "protein"])
        fat_col = find_col(["FAT(g)", "FAT_g", "FAT", "fat", "total fat"])
        energy_col = find_col(["Energy(kcal)", "Energy_kcal", "energy(kcal)", "energy", "calories"])

        df["CHO(g)"] = df[cho_col].apply(lambda v: safe_float(v, 0)) if cho_col else 0.0
        df["PRO(g)"] = df[pro_col].apply(lambda v: safe_float(v, 0)) if pro_col else 0.0
        df["FAT(g)"] = df[fat_col].apply(lambda v: safe_float(v, 0)) if fat_col else 0.0
        df["Energy(kcal)"] = df[energy_col].apply(lambda v: safe_float(v, 0)) if energy_col else 0.0

        # Category
        cat_col = find_col(["Category", "category"])
        df["Category"] = df[cat_col].astype(str) if cat_col else "Unknown"

        log.info("Loaded FEL dataset rows=%d", len(df))
        return df
    except Exception as e:
        log.exception("Failed to load dataset: %s", e)
        return pd.DataFrame()


fel = load_dataset(DATA_FILE)


# -------------------------------------------------------------------
# FEL lookup with caching
# -------------------------------------------------------------------
@lru_cache(maxsize=256)
def lookup_fel(food: str):
    food_norm = normalize_text(food)
    match_row, lookup_path = fel_find_match(food_norm)
    if match_row is None:
        log.warning("No FEL match found for %s", food_norm)
        return None
    return {
        "protein": float(match_row.get("PRO(g)", 0)),
        "fat": float(match_row.get("FAT(g)", 0)),
        "carbs": float(match_row.get("CHO(g)", 0)),
        "calories": float(match_row.get("Energy(kcal)", 0)),
        "portion": float(match_row.get("Portion(g)", 100)),
        "lookup_path": lookup_path
    }


# -------------------------------------------------------------------
# FEL matching: exact -> token-set -> contains -> fuzzy
# -------------------------------------------------------------------
def fel_find_match(name: str):
    if fel is None or fel.empty or not name:
        log.error("FEL dataset unavailable or empty for match lookup")
        return None, None
    q = normalize_text(name)

    # Exact match
    exact = fel[fel["Food"] == q]
    if not exact.empty:
        return exact.iloc[0], "FEL-exact"

    # Token-set match
    q_tokens = set(q.split())
    for _, row in fel.iterrows():
        toks = set(str(row["Food"]).split())
        if q_tokens and (q_tokens.issubset(toks) or toks.issubset(q_tokens)):
            return row, "FEL-tokenset"

    # Contains (substring)
    contains = fel[fel["Food"].str.contains(q, na=False)]
    if not contains.empty:
        return contains.iloc[0], "FEL-contains"

    # Fuzzy match
    choices = fel["Food"].dropna().astype(str).unique().tolist()
    close = difflib.get_close_matches(q, choices, n=1, cutoff=0.75)
    if close:
        matched = fel[fel["Food"] == close[0]]
        if not matched.empty:
            return matched.iloc[0], "FEL-fuzzy"

    log.warning("No match found for %s after all methods", q)
    return None, None


# -------------------------------------------------------------------
# External API Fallbacks (caching)
# -------------------------------------------------------------------
external_cache = {}  # key: (food, grams_rounded) -> dict


def cache_key(food_name: str, grams: float):
    return (normalize_text(food_name), int(round(grams)))


def cached_return(food_name: str, grams: float):
    return external_cache.get(cache_key(food_name, grams))


def store_cache(food_name: str, grams: float, value: dict):
    external_cache[cache_key(food_name, grams)] = value


def normalize_external_item(food_name: str, grams: float, src: str, calories=0, protein=0, fat=0, carbs=0):
    return {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodCategoryId": src,
        "FoodId": normalize_text(food_name),
        "Calories": round(calories, 2),
        "Protein": round(protein, 2),
        "Fat": round(fat, 2),
        "Carbs": round(carbs, 2),
        "UserId": "Unknown",
        "FoodGramAmount": float(grams),
        "Source": src
    }


def lookup_usda(food_name: str, grams: float):
    if not USDA_API_KEY:
        return None
    try:
        params = {"query": food_name, "api_key": USDA_API_KEY, "pageSize": 1}
        r = requests.get(USDA_URL, params=params, timeout=7)
        if r.status_code != 200:
            log.warning("USDA lookup status %s for %s", r.status_code, food_name)
            return None
        foods = r.json().get("foods", [])
        if not foods:
            return None
        f = foods[0]
        nutrients = {n.get("nutrientName").lower(): n.get("value") for n in f.get("foodNutrients", []) if
                     "nutrientName" in n}
        protein = safe_float(nutrients.get("protein", 0))
        fat = safe_float(nutrients.get("total lipid (fat)", 0))
        carbs = safe_float(nutrients.get("carbohydrate, by difference", 0))
        calories = calculate_atwater_kcal(protein, fat, carbs)  # Use Atwater
        scale = grams / 100.0
        return normalize_external_item(
            food_name, grams, "USDA",
            calories * scale, protein * scale, fat * scale, carbs * scale
        )
    except Exception as e:
        log.warning("USDA lookup failed for %s: %s", food_name, e)
        return None


def lookup_api_ninjas(food_name: str, grams: float):
    if not API_NINJAS_KEY:
        return None
    try:
        r = requests.get(
            NINJAS_URL,
            params={"query": f"{int(grams)} g {food_name}"},
            headers={"X-Api-Key": API_NINJAS_KEY},
            timeout=7
        )
        if r.status_code != 200:
            log.warning("API Ninjas lookup status %s for %s", r.status_code, food_name)
            return None
        arr = r.json()
        if not arr:
            return None
        d = arr[0]
        protein = safe_float(d.get("protein_g"), 0)
        fat = safe_float(d.get("fat_total_g"), 0)
        carbs = safe_float(d.get("carbohydrates_total_g"), 0)
        calories = calculate_atwater_kcal(protein, fat, carbs)  # Use Atwater
        return normalize_external_item(food_name, grams, "API_Ninjas", calories, protein, fat, carbs)
    except Exception as e:
        log.warning("API Ninjas lookup failed for %s: %s", food_name, e)
        return None


def external_fallback(food_name: str, grams: float):
    cached = cached_return(food_name, grams)
    if cached:
        return cached
    for fn in (lookup_usda, lookup_api_ninjas):
        item = fn(food_name, grams)
        if item:
            store_cache(food_name, grams, item)
            return item
    log.warning("No external data found for %s", food_name)
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
        "dataset_loaded": False if fel is None or fel.empty else True,
        "rows": int(len(fel)) if (fel is not None and not fel.empty) else 0,
        "external": {
            "usda": bool(USDA_API_KEY),
            "api_ninjas": bool(API_NINJAS_KEY)
        },
        "time": datetime.utcnow().isoformat()
    })


# -------------------------------------------------------------------
# BMI helpers and recommendation endpoint (old style with fixes)
# -------------------------------------------------------------------
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
# Nutritional info endpoint
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

        # Override with nutritionist’s pork adobo values if applicable
        if name == "pork adobo":
            nut_protein = 17.9 * (grams / 160)
            nut_fat = 8.9 * (grams / 160)
            nut_carbs = 2.7 * (grams / 160)
            nut_calories = calculate_atwater_kcal(nut_protein, nut_fat, nut_carbs)
            item["Protein"] = round(nut_protein, 2)
            item["Fat"] = round(nut_fat, 2)
            item["Carbs"] = round(nut_carbs, 2)
            item["Calories"] = round(nut_calories, 2)
            item["Note"] = "Adjusted to match nutritionist’s pork adobo values"

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
            "API_Ninjas": bool(API_NINJAS_KEY)
        }

    return jsonify(response)

# -------------------------------------------------------------------
# Gemini Food Description
# -------------------------------------------------------------------
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
            "Please identify the Filipino food shown in the image. "
            "If the image is not food or is fake food, respond with: No food is detected! "
            "Respond with the food name and its category in this format: Food Name - Category."
        ])
        return jsonify({"description": result.text})
    except Exception as e:
        return jsonify({"error": str(e)}), 500

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