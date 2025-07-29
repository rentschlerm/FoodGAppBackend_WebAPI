using FoodGappBackend_WebAPI.Models;
using FoodGappBackend_WebAPI.Repository;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Controllers
{
    [Route("api/[controller]")]
    public class FoodLoggingController : BaseController
    {
        private readonly BaseRepository<NutrientLog> _nutrientLogRepo;
        private readonly BaseRepository<FoodLog> _foodLogRepo;
        private readonly BaseRepository<Food> _foodRepo;

        public FoodLoggingController()
        {
            _nutrientLogRepo = new BaseRepository<NutrientLog>();
            _foodLogRepo = new BaseRepository<FoodLog>();
            _foodRepo = new BaseRepository<Food>();
        }

        [HttpGet("createUserFoodLog")]
        public IActionResult CreateUserFoodLog()
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
                return BadRequest(new { error = "User is not authenticated" });

            var foodLog = new FoodLog { UserId = UserId };
            var result = _foodLogMgr.CreateFoodLog(foodLog, ref ErrorMessage);

            if (result != ErrorCode.Success)
                return BadRequest(new { error = "Failed to create food log", details = ErrorMessage });

            return Ok(new { message = "User food log created successfully" });
        }

        [HttpPost("log")]
        public IActionResult LogFood([FromBody] LogFoodRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Log entry cannot be null" });

            if (string.IsNullOrWhiteSpace(request.FoodName) ||
                request.UserId <= 0 ||
                request.Grams <= 0)
                return BadRequest(new { error = "Invalid request data. Food name, user ID, and grams are required" });

            // Map meal type to category ID
            int? foodCategoryId = request.MealType?.ToLower() switch
            {
                "breakfast" => 1,
                "lunch" => 2,
                "dinner" => 3,
                _ => 1 // Default to breakfast
            };

            // Check if food exists, if not create it
            var existingFood = _foodRepo.GetAll()
                .FirstOrDefault(f => f.FoodName.ToLower() == request.FoodName.ToLower());

            Food food;
            if (existingFood == null)
            {
                // Create new food entry
                food = new Food
                {
                    FoodName = request.FoodName,
                    FoodCategoryId = foodCategoryId
                };

                if (_foodRepo.Create(food, out ErrorMessage) != ErrorCode.Success)
                    return BadRequest(new { error = "Failed to create food entry", details = ErrorMessage });
            }
            else
            {
                food = existingFood;
            }

            var nutrientLog = new NutrientLog
            {
                UserId = request.UserId,
                FoodId = food.FoodId,
                FoodCategoryId = foodCategoryId,
                Calories = "0",
                Protein = "0",
                Fat = "0",
                FoodGramAmount = request.Grams
            };

            if (_nutrientLogRepo.Create(nutrientLog, out ErrorMessage) != ErrorCode.Success)
                return BadRequest(new { error = "Failed to create nutrient log", details = ErrorMessage });

            var foodLog = new FoodLog
            {
                UserId = request.UserId,
                FoodCategoryId = foodCategoryId,
                FoodId = food.FoodId,
                NutrientLogId = nutrientLog.NutrientLogId
            };

            if (_foodLogRepo.Create(foodLog, out ErrorMessage) != ErrorCode.Success)
                return BadRequest(new { error = "Failed to create food log", details = ErrorMessage });

            return Ok(new
            {
                message = "Food logged successfully",
                foodLogId = foodLog.FoodLogId,
                nutrientLogId = nutrientLog.NutrientLogId,
                foodId = food.FoodId,
                foodName = request.FoodName,
                mealType = request.MealType,
                foodCategoryId = foodCategoryId
            });
        }

        [HttpPost("logScannedFood")]
        public IActionResult LogScannedFood([FromBody] ScannedFoodRequest request)
        {
            try
            {
                if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
                    return BadRequest(new { error = "User is not authenticated" });

                if (request == null)
                    return BadRequest(new { error = "Request cannot be null" });

                if (string.IsNullOrWhiteSpace(request.FoodName) || request.Grams <= 0)
                    return BadRequest(new { error = "Invalid request data. Food name and grams are required" });

                // Map meal type to category ID
                int? foodCategoryId = request.MealType?.ToLower() switch
                {
                    "breakfast" => 1,
                    "lunch" => 2,
                    "dinner" => 3,
                    _ => 1 // Default to breakfast
                };

                // Check if food exists, if not create it
                var existingFood = _foodRepo.GetAll()
                    .FirstOrDefault(f => f.FoodName.ToLower() == request.FoodName.ToLower());

                Food food;
                if (existingFood == null)
                {
                    // Create new food entry
                    food = new Food
                    {
                        FoodName = request.FoodName,
                        FoodCategoryId = foodCategoryId
                    };

                    if (_foodRepo.Create(food, out ErrorMessage) != ErrorCode.Success)
                        return BadRequest(new { error = "Failed to create food entry", details = ErrorMessage });
                }
                else
                {
                    food = existingFood;
                }

                // Create nutrient log with scanned data
                var nutrientLog = new NutrientLog
                {
                    UserId = UserId,
                    FoodId = food.FoodId,
                    FoodCategoryId = foodCategoryId,
                    Calories = request.Calories ?? "0",
                    Protein = request.Protein ?? "0",
                    Fat = request.Fat ?? "0",
                    Carbs = request.Carbs ?? "0", // <-- Add this
                    FoodGramAmount = request.Grams
                };

                if (_nutrientLogRepo.Create(nutrientLog, out ErrorMessage) != ErrorCode.Success)
                    return BadRequest(new { error = "Failed to create nutrient log", details = ErrorMessage });

                // Create food log entry
                var foodLog = new FoodLog
                {
                    UserId = UserId,
                    FoodCategoryId = foodCategoryId,
                    FoodId = food.FoodId,
                    NutrientLogId = nutrientLog.NutrientLogId
                };

                if (_foodLogRepo.Create(foodLog, out ErrorMessage) != ErrorCode.Success)
                    return BadRequest(new { error = "Failed to create food log", details = ErrorMessage });

                return Ok(new
                {
                    message = "Scanned food logged successfully",
                    foodLogId = foodLog.FoodLogId,
                    nutrientLogId = nutrientLog.NutrientLogId,
                    foodId = food.FoodId,
                    foodName = request.FoodName,
                    mealType = request.MealType,
                    calories = request.Calories,
                    protein = request.Protein,
                    fat = request.Fat,
                    grams = request.Grams,
                    foodCategoryId = foodCategoryId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An unexpected error occurred", details = ex.Message });
            }
        }

        [HttpGet("getUserLogs")]
        public IActionResult GetUserLogs([FromQuery] int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Valid user ID is required" });

            try
            {
                // Get all food logs for the user
                var userFoodLogs = _foodLogRepo.GetAll()
                    .Where(fl => fl.UserId == userId)
                    .ToList();

                Console.WriteLine($"Found {userFoodLogs.Count} food logs for user {userId}");

                var result = userFoodLogs.Select(fl => {
                    // Get nutrient data
                    var nutrientData = fl.NutrientLogId.HasValue ? _nutrientLogRepo.Get(fl.NutrientLogId.Value) : null;

                    // Get food data - this is where the issue might be
                    var foodData = fl.FoodId.HasValue ? _foodRepo.Get(fl.FoodId.Value) : null;

                    // Debug logging
                    Console.WriteLine($"FoodLog {fl.FoodLogId}: FoodId={fl.FoodId}, FoodData found: {foodData != null}");
                    if (foodData != null)
                    {
                        Console.WriteLine($"Food name: {foodData.FoodName}");
                    }
                    else
                    {
                        Console.WriteLine("Food data is NULL!");
                    }

                    return new
                    {
                        foodLogId = fl.FoodLogId,
                        userId = fl.UserId,
                        foodCategoryId = fl.FoodCategoryId,
                        foodId = fl.FoodId,
                        nutrientLogId = fl.NutrientLogId,
                        nutrientData = nutrientData,
                        foodData = foodData,
                        mealType = fl.FoodCategoryId == 1 ? "Breakfast" :
                                  fl.FoodCategoryId == 2 ? "Lunch" :
                                  fl.FoodCategoryId == 3 ? "Dinner" : "Breakfast"
                    };
                }).ToList();

                return Ok(new { logs = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in getUserLogs: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred while fetching logs", details = ex.Message });
            }
        }

        [HttpPut("updateNutrientLog/{nutrientLogId}")]
        public IActionResult UpdateNutrientLog(int nutrientLogId, [FromBody] UpdateNutrientRequest request)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
                return BadRequest(new { error = "User is not authenticated" });

            var nutrientLog = _nutrientLogRepo.Get(nutrientLogId);
            if (nutrientLog == null)
                return NotFound(new { error = "Nutrient log not found" });

            if (nutrientLog.UserId != UserId)
                return Forbid("You can only update your own logs");

            nutrientLog.Calories = request.Calories ?? nutrientLog.Calories;
            nutrientLog.Protein = request.Protein ?? nutrientLog.Protein;
            nutrientLog.Fat = request.Fat ?? nutrientLog.Fat;

            if (_nutrientLogRepo.Update(nutrientLogId, nutrientLog, out ErrorMessage) != ErrorCode.Success)
                return BadRequest(new { error = "Failed to update nutrient log", details = ErrorMessage });

            return Ok(new { message = "Nutrient log updated successfully" });
        }

        [HttpDelete("deleteLog/{foodLogId}")]
        public IActionResult DeleteLog(int foodLogId)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
                return BadRequest(new { error = "User is not authenticated" });

            var foodLog = _foodLogRepo.Get(foodLogId);
            if (foodLog == null)
                return NotFound(new { error = "Food log not found" });

            if (foodLog.UserId != UserId)
                return Forbid("You can only delete your own logs");

            if (foodLog.NutrientLogId.HasValue)
                _nutrientLogRepo.Delete(foodLog.NutrientLogId.Value, out var nutrientErrorMessage);

            if (_foodLogRepo.Delete(foodLogId, out ErrorMessage) != ErrorCode.Success)
                return BadRequest(new { error = "Failed to delete food log", details = ErrorMessage });

            return Ok(new { message = "Food log deleted successfully" });
        }

        [HttpGet("getUserNutritionSummary")]
        public IActionResult GetUserNutritionSummary([FromQuery] int userId, [FromQuery] DateTime? date = null)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Valid user ID is required" });

            var targetDate = date ?? DateTime.Today;

            var nutrientLogs = _nutrientLogRepo.GetAll()
                .Where(nl => nl.UserId == userId)
                .ToList();

            var summary = new
            {
                totalCalories = nutrientLogs.Sum(nl => double.TryParse(nl.Calories, out var cal) ? cal : 0),
                totalProtein = nutrientLogs.Sum(nl => double.TryParse(nl.Protein, out var prot) ? prot : 0),
                totalFat = nutrientLogs.Sum(nl => double.TryParse(nl.Fat, out var fat) ? fat : 0),
                totalEntries = nutrientLogs.Count,
                date = targetDate.ToString("yyyy-MM-dd")
            };

            return Ok(summary);
        }
    }

    public class LogFoodRequest
    {
        public int UserId { get; set; }
        public string FoodName { get; set; }
        public double Grams { get; set; }
        public string? MealType { get; set; }
        public string? Carbs { get; set; } // <-- Add this
    }

    public class UpdateNutrientRequest
    {
        public string? Calories { get; set; }
        public string? Protein { get; set; }
        public string? Fat { get; set; }
    }

    public class ScannedFoodRequest
    {
        public string FoodName { get; set; }
        public double Grams { get; set; }
        public string? Calories { get; set; }
        public string? Protein { get; set; }
        public string? Fat { get; set; }
        public string? MealType { get; set; }
        public string? Carbs { get; set; } // <-- Add this
    }
}
