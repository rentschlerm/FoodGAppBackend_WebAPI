using Microsoft.AspNetCore.Mvc;
using FoodGappBackend_WebAPI.Models;
using FoodGappBackend_WebAPI.Repository;

namespace FoodGappBackend_WebAPI.Controllers
{
    [Route("api/[controller]")]
    public class MealPlanAndRecipeController : Controller
    {
        private readonly BaseRepository<MealPlan> _mealPlanRepo;
        private readonly BaseRepository<NutrientLog> _nutrientLogRepo;

        public MealPlanAndRecipeController()
        {
            _mealPlanRepo = new BaseRepository<MealPlan>();
            _nutrientLogRepo = new BaseRepository<NutrientLog>();
        }

        [HttpPost("importMealPlan")]
        public IActionResult ImportMealPlan([FromBody] List<MealPlanImportRequest> meals)
        {
            foreach (var meal in meals)
            {
                var nutrientLog = new NutrientLog
                {
                    Calories = meal.Calories.ToString(),
                    Protein = meal.Protein.ToString(),
                    Fat = meal.Fats.ToString(),
                    Carbs = (float)meal.Carbs, // Use float, not string
                    Sugar = null,              // Set to null or a float value if available
                    UserId = meal.UserId,
                    FoodId = meal.FoodId,
                    FoodCategoryId = meal.FoodCategoryId,
                    FoodGramAmount = meal.Grams
                };

                string errMsg;
                _nutrientLogRepo.Create(nutrientLog, out errMsg);

                var mealPlan = new MealPlan
                {
                    UserId = meal.UserId,
                    Date = DateTime.Now,
                    MealType = meal.Type,
                    Recipe = meal.Name,
                    NutrientLogId = nutrientLog.NutrientLogId,
                    MicroNutrients = "", // Optional
                    AlertNotes = meal.AlertNotes
                };

                _mealPlanRepo.Create(mealPlan, out errMsg);
            }

            return Ok(new { message = "Meal plans imported successfully." });
        }
    }

    public class MealPlanImportRequest
    {
        public int UserId { get; set; }
        public string Name { get; set; }
        public double Calories { get; set; }
        public double Protein { get; set; }
        public double Fats { get; set; }
        public double Carbs { get; set; }
        public string Type { get; set; }
        public int? FoodId { get; set; }
        public int? FoodCategoryId { get; set; }
        public double Grams { get; set; }
        public string AlertNotes { get; set; }
    }
}
