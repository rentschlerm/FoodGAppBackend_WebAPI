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

        [HttpPost("log")]
        public IActionResult LogFood([FromBody] LogFoodRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Log entry cannot be null" });

            if (string.IsNullOrWhiteSpace(request.FoodName) || request.UserId <= 0 || request.Grams <= 0)
                return BadRequest(new { error = "Invalid request data. Food name, user ID, and grams are required" });

            int? foodCategoryId = request.MealType?.ToLower() switch
            {
                "breakfast" => 1,
                "lunch" => 2,
                "dinner" => 3,
                _ => 1
            };

            // Check if food already exists
            var existingFood = _foodRepo.GetAll()
                .FirstOrDefault(f => f.FoodName.ToLower() == request.FoodName.ToLower());

            Food food;
            if (existingFood == null)
            {
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

            // Create NutrientLog
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

            // Create FoodLog
            var foodLog = new FoodLog
            {
                UserId = request.UserId,
                FoodCategoryId = foodCategoryId,
                FoodId = food.FoodId,
                NutrientLogId = nutrientLog.NutrientLogId
            };

            if (_foodLogRepo.Create(foodLog, out ErrorMessage) != ErrorCode.Success)
                return BadRequest(new { error = "Failed to create food log", details = ErrorMessage });

            // Award XP for food log
            var user = _userMgr.GetUserById(request.UserId);
            if (user != null)
            {
                _userMgr.AddExperience(user, 25);
                _userMgr.UpdateUser(user, ref ErrorMessage);
            }

            return Ok(new
            {
                message = "Food logged successfully",
                foodLogId = foodLog.FoodLogId,
                nutrientLogId = nutrientLog.NutrientLogId,
                foodId = food.FoodId,
                foodName = food.FoodName,
                mealType = request.MealType,
                foodCategoryId = foodCategoryId
            });
        }

        [HttpGet("getUserLogs")]
        public IActionResult GetUserLogs([FromQuery] int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Valid user ID is required" });

            try
            {
                var userFoodLogs = _foodLogRepo.GetAll()
                    .Where(fl => fl.UserId == userId)
                    .ToList();

                var result = userFoodLogs.Select(fl =>
                {
                    var nutrientData = fl.NutrientLogId.HasValue ? _nutrientLogRepo.Get(fl.NutrientLogId.Value) : null;
                    int? foodId = nutrientData?.FoodId;

                    string foodName = "Unknown Food";
                    if (foodId.HasValue)
                    {
                        var foodData = _foodRepo.Get(foodId.Value);
                        if (foodData != null && !string.IsNullOrWhiteSpace(foodData.FoodName))
                        {
                            foodName = foodData.FoodName;
                        }
                        else
                        {
                            Console.WriteLine($"Warning: FoodName missing for FoodId {foodId.Value}");
                            foodName = $"Food Entry {foodId.Value}";
                        }
                    }

                    return new
                    {
                        foodLogId = fl.FoodLogId,
                        userId = fl.UserId,
                        foodCategoryId = fl.FoodCategoryId,
                        foodId = foodId,
                        nutrientLogId = fl.NutrientLogId,
                        nutrientData = nutrientData,
                        foodName = foodName,
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
    }

    public class LogFoodRequest
    {
        public int UserId { get; set; }
        public string FoodName { get; set; }
        public double Grams { get; set; }
        public string? MealType { get; set; }
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
