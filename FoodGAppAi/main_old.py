import os
import google.generativeai as genai
from flask import Flask, request, jsonify
from datetime import datetime, timedelta
from dotenv import load_dotenv
import os

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
#             "(approximately 10°C to 20°C). "
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


def get_recommended_foods(data):
    try:
        if not data:
            return "Empty storage."

        print("Data received:", data)

        if isinstance(data, dict):
            food_names = [data.get('foodName', 'Unknown')]
        elif isinstance(data, list):
            food_names = [item.get('foodName', 'Unknown') for item in data]
        else:
            return "Invalid data format."

        data_str = ", ".join(food_names)
        print("Food names:", data_str)

        model = genai.GenerativeModel("gemini-1.5-flash")
        result = model.generate_content(
            f"Please recommend some foods/recipes to recycle these items, make some combinations if possible: {data_str}. (Focus on filipino foods and not impossible to make recipes)"
            f"Only respond with the name of recommended recipes/foods, ingredients, and cooking instructions in "
            f"bullet forms. (Dont add note.)"
        )

        return result.text

    except Exception as e:
        return f"An error occurred: {e}"


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


@app.route('/recommend_foods', methods=['POST'])
def recommend_foods():
    try:
        data = request.get_json()

        food_list = data.get("food_list")
        if not food_list:
            return jsonify({"error": "No food list provided"}), 400

        recommended_foods = get_recommended_foods(food_list)
        print(recommended_foods)
        return jsonify({"description": recommended_foods}), 200
    except Exception as e:
        return jsonify({"error": f"An error occurred: {str(e)}"}), 500


if __name__ == "__main__":
    app.run(host='0.0.0.0', port=5000, debug=True)
