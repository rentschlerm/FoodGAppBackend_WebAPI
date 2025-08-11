using FoodGappBackend_WebAPI.Models;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Repository
{
    public class TrackingManager
    {
        private readonly BaseRepository<DailyIntake> _dailyIntakeRepo;
        private readonly BaseRepository<NutrientLog> _nutrientLogRepo;

        public TrackingManager()
        {
            _dailyIntakeRepo = new BaseRepository<DailyIntake>();
            _nutrientLogRepo = new BaseRepository<NutrientLog>();
        }

        // Get today's intake for a user
        public DailyIntake? GetTodaysIntake(int userId, DateTime today)
        {
            return _dailyIntakeRepo._table
                .FirstOrDefault(d => d.UserId == userId && d.UpdatedAt.HasValue && d.UpdatedAt.Value.Date == today.Date);
        }

        // Reset daily intake for a user (set CalorieIntake to 0)
        public bool ResetDailyIntake(int userId, DateTime today)
        {
            var intake = GetTodaysIntake(userId, today);
            if (intake != null)
            {
                intake.CalorieIntake = 0;
                intake.UpdatedAt = DateTime.Now;
                string errMsg;
                return _dailyIntakeRepo.Update(intake.DailyIntakeId, intake, out errMsg) == ErrorCode.Success;
            }
            return false;
        }

        // Add calories to today's intake
        public bool AddCaloriesToToday(int userId, int calories, DateTime today)
        {
            var intake = GetTodaysIntake(userId, today);
            if (intake != null)
            {
                intake.CalorieIntake = (intake.CalorieIntake ?? 0) + calories;
                intake.UpdatedAt = DateTime.Now;
                string errMsg;
                return _dailyIntakeRepo.Update(intake.DailyIntakeId, intake, out errMsg) == ErrorCode.Success;
            }
            else
            {
                // Create new DailyIntake if not exists
                var newIntake = new DailyIntake
                {
                    UserId = userId,
                    CalorieIntake = calories,
                    UpdatedAt = DateTime.Now
                };
                string errMsg;
                return _dailyIntakeRepo.Create(newIntake, out errMsg) == ErrorCode.Success;
            }
        }

        // Nutrient log methods (unchanged)
        public NutrientLog? GetNutrientLogById(int nutrientLogId)
        {
            return _nutrientLogRepo.Get(nutrientLogId);
        }

        public ErrorCode CreateNutrientLog(NutrientLog log, ref string errMsg)
        {
            return _nutrientLogRepo.Create(log, out errMsg);
        }
    }
}