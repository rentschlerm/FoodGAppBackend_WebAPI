import os
import google.generativeai as genai
from flask import Flask, request, jsonify
from datetime import datetime, timedelta
from dotenv import load_dotenv
import json

load_dotenv()
genai.configure(api_key=os.getenv("SECRET_KEY"))

app = Flask(__name__)


def upload_to_gemini(path, mime_type=None):
    try:
        file = genai.upload_file(path, mime_type=mime_type)
        print(f"Uploaded file '{file.display_name}' as: {file.uri}")
        return file
    except Exception as e:
        print(f"Error uploading file: {e}")
        return None


def describe_image(image_path):
    try:
        uploaded_file = upload_to_gemini(image_path, mime_type="image/jpeg")

        if uploaded_file is None:
            return "Error uploading image. Please check the file path and try again."

        model = genai.GenerativeModel("gemini-1.5-flash")

        result = model.generate_content([
            uploaded_file,
            "\n\n",
            "Please identify the Filipino food shown in the image. "
            "If the image is not food or is fake food (be strict with it), respond with: No food is detected!"

            "If there are multiple dishes, focus on the one that is most prominent and be specific with the food "
            "name (if detected is tocino and sunny side up eggs then choose the closest one only), another thing be "
            "specific with the food name for example in eggs (sunny side up, scrambled egg, boiled egg etc)."
            "Respond with the food name and its category in this format: Food Name - Category. "
            "The category must be one of the following: Bread, Dairy, "
            f"Dessert, Egg, Fruit, Meat, Noodles, Rice, Seafood, Soup, Vegetable, Fried Food. "
            f"Choose only one primary category. If a food could fit into multiple categories, just select the main or "
            f"closest category (Choose only one)."
            "Do not provide any explanations; just the name of the Filipino dish followed by its category. "
            "Always consider FILIPINO FOOD! If it resembles Filipino food, respond as Filipino food. "
        ])

        return result.text
    except Exception as e:
        return f"An error occurred: {e}"


def describe_food_text(food_name):
    try:
        if food_name is None:
            return "Error, no food name. Please try again later."

        print(food_name)

        model = genai.GenerativeModel("gemini-1.5-flash")

        result = model.generate_content(
            f"Please identify the food category of {food_name} using the following categories: Bread, Dairy, "
            f"Dessert, Egg, Fruit, Meat, Noodles, Rice, Seafood, Soup, Vegetable, Fried Food. "
            f"Choose only one primary category. If a food could fit into multiple categories, just select the main or "
            f"closest category"
            f"that best reflects the main ingredient or defining characteristic."
        )

        return result.text
    except Exception as e:
        return f"An error occurred: {e}"


def get_estimated_expiry_date(food_name, date_added, storage_method):
    try:
        if not food_name or not date_added or not storage_method:
            return "Error: Food name, date added or storage_method is missing."

        if isinstance(date_added, str):
            date_added = datetime.strptime(date_added, "%Y-%m-%d")

        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"The Filipino food item is {food_name}'. Predict its accurate shelf life."
            "Take into account: "
            f"- Selected Storage Method for this food is: {storage_method}"
            "- Common preservation methods used in Filipino cuisine (e.g., cooking and preparation techniques). "
            "- The typical shelf life of similar dishes. "
            "The shelf life should be expressed as a SINGLE WHOLE NUMBER in days (e.g., 2, 3, or 4). Do not use "
            "ranges or any other format."
            "Accuracy is critical, as this data directly impacts consumer health. Ensure your prediction is as "
            "precise and reliable as possible."
        )

        result = model.generate_content(prompt)
        result_text = result.text.strip()

        if '-' in result_text:
            shelf_life_days = result_text.split('-')[0]  # take the lower bound
        else:
            shelf_life_days = result_text

        try:
            shelf_life_days = int(shelf_life_days)
        except ValueError:
            return f"Error: Could not parse shelf life from the response: {result_text}"

        expiry_date = date_added + timedelta(days=shelf_life_days)

        print(expiry_date.strftime("%a %b %d %Y"))
        return expiry_date.strftime("%a %b %d %Y")

    except Exception as e:
        return f"Error: {str(e)}"


def get_nutritional_info(data):
    try:
        if not data:
            return "Empty storage."

        print("Data received:", data)

        # Get timestamp (from input or fallback to now)
        timestamp = data.get("date")
        if timestamp:
            try:
                parsed_date = datetime.fromisoformat(timestamp)
            except ValueError:
                return "Invalid date format. Use ISO format like YYYY-MM-DDTHH:MM:SS"
        else:
            parsed_date = datetime.now()

        print("Timestamp used:", parsed_date)

        if not isinstance(data.get("items", []), list):
            return "Invalid data format. Expected 'items' to be a list."

        food_items = []
        for item in data["items"]:
            food_name = item.get("foodName", "Unknown")
            grams = item.get("grams", 100)
            food_items.append(f"{food_name} - {grams}g")

        data_str = ", ".join(food_items)
        body_goal = data.get("body_goal", "").strip().lower()

        print("Food and grams:", data_str)
        print("Body goal:", body_goal)

        model = genai.GenerativeModel("gemini-1.5-flash")

        goal_prompt = ""
        if body_goal == "lose weight":
            goal_prompt = "Keep in mind that the user is trying to lose weight, so prefer lower-calorie and high-protein interpretations."
        elif body_goal == "maintain weight":
            goal_prompt = "The user wants to maintain weight, so aim for balanced calories and macros."
        else:
            goal_prompt = "Use a general nutritional interpretation."

        prompt = (
            f"For the following Filipino foods with their respective weights, estimate the amount of the following nutrients:\n"
            f"- Calories (kcal)\n"
            f"- Protein (g)\n"
            f"- Total Fat (g)\n"
            f"- Carbohydrates (g)\n"
            f"- Cholesterol (mg)\n"
            f"- Sodium (mg)\n"
            f"- Dietary Fiber (g)\n"
            f"- Sugar (g)\n"
            f"- Vitamin D (mcg)\n"
            f"- Calcium (mg)\n"
            f"- Iron (mg)\n"
            f"- Potassium (mg)\n"
            f"- Vitamin A (mcg)\n"
            f"- Vitamin C (mg)\n\n"
            f"{goal_prompt}\n\n"
            f"Here are the foods:\n{data_str}\n\n"
            f"Respond using this exact format per food:\n"
            f"- Food Name (grams g):\n"
            f"  Calories: __ kcal\n"
            f"  Protein: __ g\n"
            f"  Total Fat: __ g\n"
            f"  Carbohydrates: __ g\n"
            f"  Cholesterol: __ mg\n"
            f"  Sodium: __ mg\n"
            f"  Dietary Fiber: __ g\n"
            f"  Sugar: __ g\n"
            f"  Vitamin D: __ mcg\n"
            f"  Calcium: __ mg\n"
            f"  Iron: __ mg\n"
            f"  Potassium: __ mg\n"
            f"  Vitamin A: __ mcg\n"
            f"  Vitamin C: __ mg\n"

            f"Use realistic estimations based on Filipino nutritional values. Do not include explanations or extra notes."
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


# NEW WELLNŪ STUDY OBJECTIVE FUNCTIONS

def analyze_nutrient_deficiencies(user_data):
    """Analyze user's nutritional intake for deficiencies - WellNū Objective 1"""
    try:
        if not user_data:
            return "No user data provided."

        age = user_data.get("age", 25)
        gender = user_data.get("gender", "unknown")
        daily_logs = user_data.get("daily_nutrition", [])
        bmi = user_data.get("bmi", 22)

        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"Analyze the following user's nutritional intake for potential deficiencies:\n"
            f"- Age: {age}\n"
            f"- Gender: {gender}\n"
            f"- BMI: {bmi}\n"
            f"- Recent nutrition logs: {daily_logs}\n\n"
            f"Based on Filipino dietary patterns and this user's profile, identify:\n"
            f"1. Potential nutrient deficiencies\n"
            f"2. Health risks associated with current diet\n"
            f"3. Specific recommendations for Brgy. Looc, Mandaue City context\n"
            f"4. Priority nutrients to focus on\n\n"
            f"Format as JSON with sections: deficiencies, risks, recommendations, priorities"
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


def generate_personalized_recommendations(user_profile, current_nutrition):
    """Generate personalized dietary recommendations - WellNū Objective 2"""
    try:
        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"Generate personalized dietary recommendations for this Filipino user:\n"
            f"Profile: {user_profile}\n"
            f"Current nutrition: {current_nutrition}\n\n"
            f"Provide recommendations for:\n"
            f"1. Daily calorie targets based on BMI and goals\n"
            f"2. Macronutrient distribution (protein, carbs, fats)\n"
            f"3. Key micronutrients to focus on\n"
            f"4. Specific Filipino foods to include/avoid\n"
            f"5. Meal timing and portion suggestions\n"
            f"6. Local food alternatives available in Cebu\n\n"
            f"Consider Filipino cultural preferences and local food availability."
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


def provide_nutrition_education(topic, user_level="beginner"):
    """Provide nutrition education content - WellNū Objective 3"""
    try:
        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"Provide nutrition education content about: {topic}\n"
            f"User level: {user_level}\n"
            f"Context: Filipino nutrition education for Brgy. Looc, Mandaue City\n\n"
            f"Include:\n"
            f"1. Simple explanation of the topic\n"
            f"2. Why it's important for Filipino health\n"
            f"3. Practical tips with local food examples\n"
            f"4. Common myths vs facts\n"
            f"5. Action steps users can take immediately\n\n"
            f"Make it culturally relevant and easy to understand."
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


def suggest_local_food_alternatives(target_nutrients, dietary_restrictions=None):
    """Suggest local food alternatives - WellNū Objective 4"""
    try:
        model = genai.GenerativeModel("gemini-1.5-flash")

        restrictions_text = f"Dietary restrictions: {dietary_restrictions}" if dietary_restrictions else "No dietary restrictions"

        prompt = (
            f"Suggest Filipino food alternatives rich in: {target_nutrients}\n"
            f"{restrictions_text}\n"
            f"Focus on foods commonly available in Cebu/Mandaue markets\n\n"
            f"Provide:\n"
            f"1. Top 5 local food sources for each nutrient\n"
            f"2. Approximate nutrient content per serving\n"
            f"3. Cost-effective options for low-income families\n"
            f"4. Preparation tips to maximize nutrient retention\n"
            f"5. Seasonal availability information\n"
            f"6. Cultural dishes that incorporate these foods\n\n"
            f"Prioritize affordable, accessible options."
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


def generate_meal_plan(user_profile, duration_days=7):
    """Generate culturally appropriate meal plans - WellNū Objective 5"""
    try:
        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"Create a {duration_days}-day Filipino meal plan for:\n"
            f"User profile: {user_profile}\n\n"
            f"Requirements:\n"
            f"1. Use primarily Filipino dishes and ingredients\n"
            f"2. Consider budget constraints typical in Brgy. Looc\n"
            f"3. Include local Cebu specialties when appropriate\n"
            f"4. Balance nutrition with cultural preferences\n"
            f"5. Provide shopping list with estimated costs\n"
            f"6. Include preparation time for working families\n"
            f"7. Suggest meal prep strategies\n\n"
            f"Format: Day-by-day breakdown with recipes and nutrition summary"
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


def assess_community_health_trends(community_data):
    """Analyze community health patterns - WellNū Objective 6"""
    try:
        model = genai.GenerativeModel("gemini-1.5-flash")

        prompt = (
            f"Analyze health trends for Brgy. Looc community based on:\n"
            f"Community data: {community_data}\n\n"
            f"Provide insights on:\n"
            f"1. Common nutritional deficiencies in the community\n"
            f"2. Dietary patterns and their health implications\n"
            f"3. Socioeconomic factors affecting nutrition\n"
            f"4. Recommendations for community-level interventions\n"
            f"5. Priority health areas to address\n"
            f"6. Cultural factors influencing food choices\n\n"
            f"Focus on actionable insights for local health programs."
        )

        result = model.generate_content(prompt)
        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


# ENHANCED API ENDPOINTS

@app.route('/describe_image', methods=['POST'])
def api_describe_image():
    if 'file' not in request.files:
        return jsonify({"error": "No file part"}), 400

    file = request.files['file']
    file_path = os.path.join("uploads", file.filename)
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    file.save(file_path)

    description = describe_image(file_path)

    return jsonify({"description": description})


@app.route('/get_estimated_expiry_date', methods=['POST'])
def get_expiry_date():
    try:
        data = request.get_json()
        food_name = data.get("food_name")
        date_added = data.get("date_added")  # format: YYYY-MM-DD
        storage_method = data.get("storage_method")

        expiry_date = get_estimated_expiry_date(food_name, date_added, storage_method)

        return jsonify({"expiry_date": expiry_date}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/identify_food_category', methods=['POST'])
def identify_food_category():
    try:
        data = request.get_json()
        food_name = data.get("food_name")

        category = describe_food_text(food_name)

        return jsonify({"description": category}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/get_nutritional_info', methods=['POST'])
def nutritional_info():
    try:
        data = request.get_json()
        response = get_nutritional_info(data)
        return jsonify({"nutritional_info": response}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


# NEW WELLNŪ STUDY ENDPOINTS

@app.route('/analyze_deficiencies', methods=['POST'])
def analyze_deficiencies():
    """Endpoint for nutrient deficiency analysis"""
    try:
        data = request.get_json()
        analysis = analyze_nutrient_deficiencies(data)
        return jsonify({"deficiency_analysis": analysis}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/personalized_recommendations', methods=['POST'])
def personalized_recommendations():
    """Endpoint for personalized dietary recommendations"""
    try:
        data = request.get_json()
        user_profile = data.get("user_profile", {})
        current_nutrition = data.get("current_nutrition", {})

        recommendations = generate_personalized_recommendations(user_profile, current_nutrition)
        return jsonify({"recommendations": recommendations}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/nutrition_education', methods=['POST'])
def nutrition_education():
    """Endpoint for nutrition education content"""
    try:
        data = request.get_json()
        topic = data.get("topic", "basic_nutrition")
        user_level = data.get("level", "beginner")

        education_content = provide_nutrition_education(topic, user_level)
        return jsonify({"education_content": education_content}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/local_food_alternatives', methods=['POST'])
def local_food_alternatives():
    """Endpoint for local food alternative suggestions"""
    try:
        data = request.get_json()
        target_nutrients = data.get("target_nutrients", [])
        dietary_restrictions = data.get("dietary_restrictions")

        alternatives = suggest_local_food_alternatives(target_nutrients, dietary_restrictions)
        return jsonify({"food_alternatives": alternatives}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/generate_meal_plan', methods=['POST'])
def meal_plan():
    """Endpoint for generating meal plans"""
    try:
        data = request.get_json()
        user_profile = data.get("user_profile", {})
        duration = data.get("duration_days", 7)

        meal_plan = generate_meal_plan(user_profile, duration)
        return jsonify({"meal_plan": meal_plan}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


@app.route('/community_health_analysis', methods=['POST'])
def community_health_analysis():
    """Endpoint for community health trend analysis"""
    try:
        data = request.get_json()
        community_data = data.get("community_data", {})

        analysis = assess_community_health_trends(community_data)
        return jsonify({"community_analysis": analysis}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


# HEALTH STATUS ENDPOINT
@app.route('/health_check', methods=['GET'])
def health_check():
    """Simple health check endpoint"""
    return jsonify({
        "status": "healthy",
        "service": "WellNū Nutrition API",
        "version": "2.0.0",
        "features": [
            "Food Recognition",
            "Nutritional Analysis",
            "Deficiency Detection",
            "Personalized Recommendations",
            "Nutrition Education",
            "Local Food Alternatives",
            "Meal Planning",
            "Community Health Analysis"
        ]
    }), 200


if __name__ == "__main__":
    app.run(host='0.0.0.0', port=5000, debug=True)