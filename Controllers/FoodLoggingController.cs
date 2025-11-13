using FoodGappBackend_WebAPI.Models;
using FoodGappBackend_WebAPI.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private readonly BaseRepository<DailyIntake> _dailyIntakeRepo;

        public FoodLoggingController()
        {
            _nutrientLogRepo = new BaseRepository<NutrientLog>();
            _foodLogRepo = new BaseRepository<FoodLog>();
            _foodRepo = new BaseRepository<Food>();
            _dailyIntakeRepo = new BaseRepository<DailyIntake>();
        }

        [HttpPost("log")]
        public IActionResult LogFood([FromBody] LogFoodRequest request)
        {
            if (request == null)
                return BadRequest(new { error = "Log entry cannot be null" });

            if (string.IsNullOrWhiteSpace(request.FoodName) || request.UserId <= 0 || request.Grams <= 0)
                return BadRequest(new { error = "Invalid request data. Food name, user ID, and grams are required" });

            // Use database transaction to prevent concurrency issues
            using var transaction = _db.Database.BeginTransaction();

            try
            {
                int? foodCategoryId = request.MealType?.ToLower() switch
                {
                    "breakfast" => 1,
                    "lunch" => 2,
                    "dinner" => 3,
                    _ => 1
                };

                // Check if food already exists
                var existingFood = _foodRepo.GetAll()
                    .FirstOrDefault(f => f.FoodName != null && f.FoodName.ToLower() == request.FoodName.ToLower());

                Food food;
                if (existingFood == null)
                {
                    food = new Food
                    {
                        FoodName = request.FoodName,
                        FoodCategoryId = foodCategoryId
                    };

                    if (_foodRepo.Create(food, out ErrorMessage) != ErrorCode.Success)
                    {
                        transaction.Rollback();
                        return BadRequest(new { error = "Failed to create food entry", details = ErrorMessage });
                    }
                }
                else
                {
                    food = existingFood;
                }

                // Create NutrientLog
                var nutrientLog = new NutrientLog
                {
                    Calories = request.Calories ?? "0",
                    Protein = request.Protein ?? "0",
                    Fat = request.Fat ?? "0",
                    Carbs = TryParseDouble(request.Carbs),
                    Sugar = TryParseDouble(request.Sugar),
                    MicroNutrients = request.MicroNutrients,
                    UserId = request.UserId,
                    FoodId = food.FoodId,
                    FoodCategoryId = foodCategoryId,
                    FoodGramAmount = request.Grams,
                    UpdatedAt = DateTime.UtcNow
                };

                if (_nutrientLogRepo.Create(nutrientLog, out ErrorMessage) != ErrorCode.Success)
                {
                    transaction.Rollback();
                    return BadRequest(new { error = "Failed to create nutrient log", details = ErrorMessage });
                }

                // Create FoodLog
                var foodLog = new FoodLog
                {
                    UserId = request.UserId,
                    FoodCategoryId = foodCategoryId,
                    FoodId = food.FoodId,
                    NutrientLogId = nutrientLog.NutrientLogId
                };

                if (_foodLogRepo.Create(foodLog, out ErrorMessage) != ErrorCode.Success)
                {
                    transaction.Rollback();
                    return BadRequest(new { error = "Failed to create food log", details = ErrorMessage });
                }

                // --- DailyIntake logic with retry mechanism ---
                int caloriesToAdd = 0;
                if (!string.IsNullOrWhiteSpace(nutrientLog.Calories))
                    int.TryParse(nutrientLog.Calories, out caloriesToAdd);

                var today = DateTime.UtcNow.Date;

                // Use lock or retry mechanism for DailyIntake to prevent concurrency
                var dailyIntake = _dailyIntakeRepo.GetAll()
                    .FirstOrDefault(di => di.UserId == request.UserId && di.UpdatedAt.HasValue && di.UpdatedAt.Value.Date == today);

                if (dailyIntake != null)
                {
                    dailyIntake.CalorieIntake = (dailyIntake.CalorieIntake ?? 0) + caloriesToAdd;
                    dailyIntake.UpdatedAt = DateTime.UtcNow;

                    // Retry mechanism for concurrency
                    int retryCount = 0;
                    while (retryCount < 3)
                    {
                        try
                        {
                            if (_dailyIntakeRepo.Update(dailyIntake.DailyIntakeId, dailyIntake, out ErrorMessage) == ErrorCode.Success)
                                break;

                            retryCount++;
                            if (retryCount >= 3)
                            {
                                transaction.Rollback();
                                return BadRequest(new { error = "Failed to update daily intake after retries", details = ErrorMessage });
                            }

                            Thread.Sleep(100); // Short delay before retry
                        }
                        catch (DbUpdateConcurrencyException)
                        {
                            retryCount++;
                            if (retryCount >= 3)
                            {
                                transaction.Rollback();
                                return StatusCode(409, new { error = "Concurrency conflict updating daily intake" });
                            }

                            // Refresh the entity and try again
                            dailyIntake = _dailyIntakeRepo.GetAll()
                                .FirstOrDefault(di => di.UserId == request.UserId && di.UpdatedAt.HasValue && di.UpdatedAt.Value.Date == today);

                            if (dailyIntake != null)
                            {
                                dailyIntake.CalorieIntake = (dailyIntake.CalorieIntake ?? 0) + caloriesToAdd;
                                dailyIntake.UpdatedAt = DateTime.UtcNow;
                            }

                            Thread.Sleep(100);
                        }
                    }
                }
                else
                {
                    var newDailyIntake = new DailyIntake
                    {
                        UserId = request.UserId,
                        CalorieIntake = caloriesToAdd,
                        UpdatedAt = DateTime.UtcNow
                    };
                    if (_dailyIntakeRepo.Create(newDailyIntake, out ErrorMessage) != ErrorCode.Success)
                    {
                        transaction.Rollback();
                        return BadRequest(new { error = "Failed to create daily intake", details = ErrorMessage });
                    }
                }

                // Award XP for food log
                var user = _userMgr.GetUserById(request.UserId);
                if (user != null)
                {
                    try
                    {
                        _userMgr.AddExperience(user, 25);
                        _userMgr.UpdateUser(user, ref ErrorMessage);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // XP update failure shouldn't fail the whole operation
                        // Log the error but continue
                        Console.WriteLine($"XP update failed for user {request.UserId}: Concurrency conflict");
                    }
                }

                // Commit the transaction
                transaction.Commit();

                // SINGLE return statement with all fields
                return Ok(new
                {
                    message = "Food logged successfully",
                    foodLogId = foodLog.FoodLogId,
                    nutrientLogId = nutrientLog.NutrientLogId,
                    foodId = food.FoodId,
                    foodName = food.FoodName,
                    mealType = request.MealType,
                    foodCategoryId = foodCategoryId,
                    sugar = nutrientLog.Sugar,
                    microNutrients = nutrientLog.MicroNutrients
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"Error in LogFood: {ex.Message}");
                return StatusCode(500, new { error = "An error occurred while logging food", details = ex.Message });
            }
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
                        nutrientData = nutrientData != null ? new
                        {
                            calories = nutrientData.Calories,
                            protein = nutrientData.Protein,
                            fat = nutrientData.Fat,
                            carbs = nutrientData.Carbs,
                            sugar = nutrientData.Sugar,
                            microNutrients = nutrientData.MicroNutrients,
                            foodGramAmount = nutrientData.FoodGramAmount,
                            updatedAt = nutrientData.UpdatedAt
                        } : null,
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

        [HttpGet("getDailyIntake")]
        public IActionResult GetDailyIntake([FromQuery] int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Valid user ID is required" });

            var today = DateTime.UtcNow.Date;
            var dailyIntake = _dailyIntakeRepo.GetAll()
                .FirstOrDefault(di => di.UserId == userId && di.UpdatedAt.HasValue && di.UpdatedAt.Value.Date == today);

            int calorieIntake = dailyIntake?.CalorieIntake ?? 0;

            return Ok(new { calorieIntake });
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

        private static double? TryParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return double.TryParse(value, out var result) ? result : null;
        }
    }


    public class LogFoodRequest
    {
        public int UserId { get; set; }
        public required string FoodName { get; set; }
        public double Grams { get; set; }
        public string? MealType { get; set; }
        public string? Calories { get; set; }
        public string? Protein { get; set; }
        public string? Fat { get; set; }
        public string? Carbs { get; set; }
        public string? Sugar { get; set; }
        public string? MicroNutrients { get; set; }
    }

    public class UpdateNutrientRequest
    {
        public string? Calories { get; set; }
        public string? Protein { get; set; }
        public string? Fat { get; set; }
        public string? Sugar { get; set; }
        public string? MicroNutrients { get; set; }
    }

    public class ScannedFoodRequest
    {
        public required string FoodName { get; set; }
        public double Grams { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Protein { get; set; }
        public string? Fat { get; set; }
        public string? MealType { get; set; }
        public string? Carbs { get; set; }
        public string? Sugar { get; set; }
        public string? MicroNutrients { get; set; }
    }
}
