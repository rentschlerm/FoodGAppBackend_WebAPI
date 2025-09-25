import os
import uuid
import logging
from datetime import datetime
from functools import lru_cache
import re
import difflib
import google.generativeai as genai
import pandas as pd
import requests
from flask import Flask, request, jsonify
from flask_cors import CORS
from dotenv import load_dotenv
import mimetypes
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
genai.configure(api_key=os.getenv("GEMINI_SECRET_KEY"))
if not USDA_API_KEY:
    log.error("USDA_API_KEY not set (USDA lookup disabled)")
if not API_NINJAS_KEY:
    log.error("API_NINJAS_KEY not set (API Ninjas lookup disabled)")

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
GEMINI_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent"
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
        portion_col = cols.get("foodgramamount")
        df["Portion(g)"] = df[portion_col].apply(lambda v: safe_float(v, 100)) if portion_col else 100.0

        # Nutrient columns
        def find_col(cands):
            for c in cands:
                if c in cols:
                    return cols[c]
            return None

        cho_col = find_col(["carbs"])
        pro_col = find_col(["protein"])
        fat_col = find_col(["fat"])
        energy_col = find_col(["calories"])
        sugar_col = find_col(["sugar"])
        micronutrients_col = find_col(["micronutrients"])

        df["CHO(g)"] = df[cho_col].apply(lambda v: safe_float(v, 0)) if cho_col else 0.0
        df["PRO(g)"] = df[pro_col].apply(lambda v: safe_float(v, 0)) if pro_col else 0.0
        df["FAT(g)"] = df[fat_col].apply(lambda v: safe_float(v, 0)) if fat_col else 0.0
        df["Energy(kcal)"] = df[energy_col].apply(lambda v: safe_float(v, 0)) if energy_col else 0.0
        df["Sugar(g)"] = df[sugar_col].apply(lambda v: safe_float(v, 0)) if sugar_col else 0.0
        df["MicroNutrients"] = df[micronutrients_col].astype(str) if micronutrients_col else ""

        # Category
        cat_col = find_col(["foodcategoryid"])
        df["Category"] = df[cat_col].astype(str) if cat_col else "Unknown"

        # Source
        src_col = find_col(["source"])
        df["Source"] = df[src_col].astype(str) if src_col else "FEL"

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
        "Protein": float(match_row.get("PRO(g)", 0)),
        "Fat": float(match_row.get("FAT(g)", 0)),
        "Carbs": float(match_row.get("CHO(g)", 0)),
        "Calories": float(match_row.get("Energy(kcal)", 0)),
        "Sugar": float(match_row.get("Sugar(g)", 0)),  # Add Sugar support
        "Portion": float(match_row.get("Portion(g)", 100)),
        "MicroNutrients": str(match_row.get("MicroNutrients", "")),  # Add MicroNutrients support
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


def normalize_external_item(food_name: str, grams: float, src: str, calories=0, protein=0, fat=0, carbs=0, sugar=0, micronutrients=""):
    return {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodCategoryId": src,
        "FoodId": normalize_text(food_name),
        "Calories": round(calories, 2),
        "Protein": round(protein, 2),
        "Fat": round(fat, 2),
        "Carbs": round(carbs, 2),
        "Sugar": round(sugar, 2),  # Add Sugar field
        "MicroNutrients": micronutrients,  # Add MicroNutrients field
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

        # Debug: Log all available nutrient names for troubleshooting
        log.info("Available nutrients for %s: %s", food_name, list(nutrients.keys()))

        protein = safe_float(nutrients.get("protein", 0))
        fat = safe_float(nutrients.get("total lipid (fat)", 0))
        carbs = safe_float(nutrients.get("carbohydrate, by difference", 0))

        # Enhanced sugar extraction with more field variations and debugging
        sugar = 0
        sugar_candidates = [
            "sugars, total including nlea",
            "sugars, total",
            "sugars, added",
            "total sugars",
            "sugar",
            "sugars",
            "sugars, by summation",
            "carbohydrate, by summation"
        ]

        # Debug: Show what sugar fields are available
        available_sugar_fields = [k for k in nutrients.keys() if "sugar" in k or "carbohydrate" in k]
        if available_sugar_fields:
            log.info("Sugar-related fields for %s: %s", food_name, available_sugar_fields)

        for candidate in sugar_candidates:
            sugar = safe_float(nutrients.get(candidate, 0))
            if sugar > 0:
                log.info("Found sugar for %s using field '%s': %s", food_name, candidate, sugar)
                break

        # If still no sugar found, try partial matching
        if sugar == 0:
            for nutrient_name, value in nutrients.items():
                if "sugar" in nutrient_name and safe_float(value, 0) > 0:
                    sugar = safe_float(value, 0)
                    log.info("Found sugar for %s using partial match '%s': %s", food_name, nutrient_name, sugar)
                    break

        # FIXED: Build micronutrients string with proper scaling - scale BEFORE converting units
        micronutrients_list = []
        scale = grams / 100.0  # Calculate scale factor once
        
        micro_map = {
            "fiber, total dietary": ("Fiber", 1, "g"),  # already in g
            "sodium, na": ("Sodium", 1, "mg"),  # keep in mg
            "vitamin c, total ascorbic acid": ("Vit C", 1, "mg"),  # keep in mg
            "vitamin a, rae": ("Vit A", 1, "mcg"),  # keep in mcg
            "calcium, ca": ("Calcium", 1, "mg"),  # keep in mg
            "iron, fe": ("Iron", 1, "mg"),  # keep in mg
            "potassium, k": ("Potassium", 1, "mg"),  # keep in mg
            "cholesterol": ("Cholesterol", 1, "mg"),  # add cholesterol
            "magnesium, mg": ("Magnesium", 1, "mg")  # add magnesium
        }
        
        for usda_name, (friendly_name, divisor, unit) in micro_map.items():
            val = safe_float(nutrients.get(usda_name, 0))
            if val > 0:
                # Scale the value based on portion size (USDA values are per 100g)
                scaled_val = val * scale
                # Only show if the scaled value is meaningful
                if scaled_val >= 0.1:
                    micronutrients_list.append(f"{friendly_name}: {scaled_val:.1f}{unit}")
        
        micronutrients = ", ".join(micronutrients_list)
        log.info("Micronutrients for %s (%sg): %s", food_name, grams, micronutrients)

        calories = calculate_atwater_kcal(protein, fat, carbs)
        return normalize_external_item(
            food_name, grams, "USDA",
            calories * scale, protein * scale, fat * scale, carbs * scale, sugar * scale, micronutrients
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
        sugar = safe_float(d.get("sugar_g"), 0)
        
        # FIXED: API Ninjas already returns values for the requested portion size, no additional scaling needed
        micronutrients_list = []
        micro_data = [
            ("Fiber", d.get("fiber_g"), "g"),
            ("Sodium", d.get("sodium_mg"), "mg"),
            ("Potassium", d.get("potassium_mg"), "mg"),
            ("Vit A", d.get("vitamin_a_mcg"), "mcg"),
            ("Vit C", d.get("vitamin_c_mg"), "mg"),
            ("Calcium", d.get("calcium_mg"), "mg"),
            ("Iron", d.get("iron_mg"), "mg"),
            ("Cholesterol", d.get("cholesterol_mg"), "mg")
        ]
        for name, val, unit in micro_data:
            val = safe_float(val, 0)
            if val > 0:
                micronutrients_list.append(f"{name}: {val:.1f}{unit}")
        micronutrients = ", ".join(micronutrients_list)
        log.info("Micronutrients for %s (%sg): %s", food_name, grams, micronutrients)
        
        calories = calculate_atwater_kcal(protein, fat, carbs)
        return normalize_external_item(food_name, grams, "API_Ninjas", calories, protein, fat, carbs, sugar, micronutrients)
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
# Unified lookup
# -------------------------------------------------------------------
def unified_lookup(food: str, grams: float):
    food_norm = normalize_text(food)
    sources = []

    # 1. FEL lookup - grams is already the edible portion
    fel_data = lookup_fel(food_norm)
    if fel_data and any([fel_data["Protein"], fel_data["Fat"], fel_data["Carbs"]]):
        sources.append(fel_data["lookup_path"])
        portion = fel_data.get("Portion", 100) or 100
        
        # Scale based on edible portion (grams) vs FEL portion
        scale = grams / portion
        log.info("FEL scaling for %s: %s edible grams / %s FEL portion = %s factor", food_norm, grams, portion, scale)
        
        base = {
            "Protein": round(fel_data["Protein"] * scale, 2),
            "Fat": round(fel_data["Fat"] * scale, 2),
            "Carbs": round(fel_data["Carbs"] * scale, 2),
            "Calories": round(fel_data["Calories"] * scale, 2),
            "Sugar": round(fel_data["Sugar"] * scale, 2),
            "MicroNutrients": fel_data["MicroNutrients"]
        }
        
        # NEW: If FEL doesn't have micronutrients, try external APIs for micronutrients only
        if not base["MicroNutrients"] or base["MicroNutrients"].strip() == "":
            log.info("FEL has macros for %s but no micronutrients, trying external APIs for edible portion", food_norm)
            
            # Try USDA for micronutrients using edible portion
            usda_data = lookup_usda(food_norm, grams)
            if usda_data and usda_data.get("MicroNutrients"):
                base["MicroNutrients"] = usda_data["MicroNutrients"]
                sources.append("USDA-micro")
            else:
                # Try API Ninjas for micronutrients using edible portion
                nin_data = lookup_api_ninjas(food_norm, grams)
                if nin_data and nin_data.get("MicroNutrients"):
                    base["MicroNutrients"] = nin_data["MicroNutrients"]
                    sources.append("Ninjas-micro")
        
    else:
        # 2. USDA lookup - grams is edible portion
        usda_data = lookup_usda(food_norm, grams)
        if usda_data:
            sources.append("USDA")
            base = usda_data
        else:
            # 3. API Ninjas lookup - grams is edible portion
            nin_data = lookup_api_ninjas(food_norm, grams)
            if nin_data:
                sources.append("API_Ninjas")
                base = nin_data
            else:
                sources.append("None")
                base = {"Protein": 0, "Fat": 0, "Carbs": 0, "Calories": 0, "Sugar": 0, "MicroNutrients": ""}

    # Compute calories with Atwater if missing or zero
    if base["Calories"] <= 0:
        base["Calories"] = round(calculate_atwater_kcal(base["Protein"], base["Fat"], base["Carbs"]), 2)
        sources.append("Atwater")

    result = {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodId": food_norm,
        "FoodCategoryId": "Unknown",
        "Calories": base["Calories"],
        "Protein": base["Protein"],
        "Fat": base["Fat"],
        "Carbs": base["Carbs"],
        "Sugar": base["Sugar"],
        "MicroNutrients": base["MicroNutrients"],
        "UserId": "Unknown",
        "FoodGramAmount": float(grams),  # This is the edible portion
        "Source": "+".join(sources),
        "LookupPath": sources[0] if sources else "None"
    }

    # Cache external results
    if "FEL" not in sources[0]:
        store_cache(food, grams, result)

    return result


# -------------------------------------------------------------------
# Scale row function
# -------------------------------------------------------------------
def scale_row(row, grams=100.0):
    portion = safe_float(row.get("Portion(g)", 100), 100)
    factor = grams / portion
    return {
        "NutrientLogId": str(uuid.uuid4()),
        "FoodCategoryId": row.get("Category", "Unknown"),
        "FoodId": str(row.get("Food_raw", "unknown")).lower(),
        "Calories": round(safe_float(row.get("Energy(kcal)", 0)) * factor, 2),
        "Protein": round(safe_float(row.get("PRO(g)", 0)) * factor, 2),
        "Fat": round(safe_float(row.get("FAT(g)", 0)) * factor, 2),
        "Carbs": round(safe_float(row.get("CHO(g)", 0)) * factor, 2),
        "Sugar": round(safe_float(row.get("Sugar(g)", 0)) * factor, 2),  # Add Sugar scaling
        "MicroNutrients": str(row.get("MicroNutrients", "")),  # Add MicroNutrients
        "UserId": "Unknown",
        "FoodGramAmount": grams,
        "Source": "FEL"
    }


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
# BMI helpers and recommendation endpoint
# -------------------------------------------------------------------
def bmi_category(bmi: float):
    if bmi < 18.5:
        return "Underweight", [5]
    if bmi < 24.9:
        return "Normal", [6]
    if bmi < 29.9:
        return "Overweight", [7]
    return "Obese", [8]


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
            food_name = str(row.get("Food_raw", "unknown"))
            grams = safe_float(row.get("Portion(g)", 100))
            item = unified_lookup(food_name, grams)
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

    log.info("Incoming nutritional query items=%s", items)

    for raw in items:
        name = str(raw.get("foodName", "unknown")).strip().lower()
        edible_grams = safe_float(raw.get("grams"), 100)  # This is already the edible portion
        
        log.info("Processing %s with %s edible grams", name, edible_grams)

        # Use edible grams directly for nutritional lookup
        item = unified_lookup(name, edible_grams)
        item["OriginalName"] = name

        # Alerts for high values based on edible portion
        if item["Calories"] > 800:
            alerts.append(f"High calories: {item['FoodId']} ({item['Calories']} kcal)")
        if item["Fat"] > 30:
            alerts.append(f"High fat: {item['FoodId']} ({item['Fat']}g)")

        results.append(item)
        
        log.info(
            "Computed %s edible_grams=%.2f src=%s cal=%.2f prot=%.2f fat=%.2f carbs=%.2f sugar=%.2f",
            item["FoodId"],
            edible_grams,
            item["Source"],
            item["Calories"],
            item["Protein"],
            item["Fat"],
            item["Carbs"],
            item["Sugar"]
        )

    response = {
        "foods": results,
        "body_goal_note": "Philippines FEL standard used where available. External fallbacks applied only if no FEL match. All calculations based on edible portion."
    }
    if alerts:
        response["realtime_alert"] = True
        response["alert_reason"] = alerts
    if include_sources:
        response["sources_used"] = {
            "USDA": bool(USDA_API_KEY),
            "api_ninjas": bool(API_NINJAS_KEY)
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
# Gemini Describe Image
# -------------------------------------------------------------------
@app.route('/describe_image', methods=['POST'])
def api_describe_image():
    if 'file' not in request.files:
        return jsonify({"error": "No file part"}), 400

    file = request.files['file']
    if file.filename == "":
        return jsonify({"error": "No selected file"}), 400

    upload_dir = "uploads"
    os.makedirs(upload_dir, exist_ok=True)
    file_path = os.path.join(upload_dir, file.filename)
    file.save(file_path)

    try:
        # Detect MIME type dynamically
        mime_type, _ = mimetypes.guess_type(file_path)
        if not mime_type:
            mime_type = "image/jpeg"

        uploaded_file = genai.upload_file(file_path, mime_type=mime_type)

        # Use safer, simpler prompt
        prompt = (
            "Identify the food in this image. Respond only in the format: Food Name - Category. "
            "If it is not food, respond: No food detected."
        )

        model = genai.GenerativeModel("gemini-1.5-flash")  # or "gemini-1.5-flash" if you want
        result = model.generate_content([uploaded_file, "\n\n", prompt])

        return jsonify({"description": result.text})

    except Exception as e:
        log.exception("Error in /describe_image")
        return jsonify({"error": str(e)}), 500
# -------------------------------------------------------------------
if __name__ == "__main__":
    log.info("Starting server on 0.0.0.0:%d (dataset_loaded=%s)", DEFAULT_PORT, fel is not None)
    app.run(host="0.0.0.0", port=DEFAULT_PORT, debug=True)
