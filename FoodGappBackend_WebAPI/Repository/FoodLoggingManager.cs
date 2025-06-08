using FoodGappBackend_WebAPI.Models;
using Microsoft.AspNetCore.Connections.Features;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Repository
{

    public class FoodLoggingManager
    {
        private readonly BaseRepository<FoodLog> _foodLogRepo;

        public FoodLoggingManager()
        {
            _foodLogRepo = new BaseRepository<FoodLog>();
        }

        public FoodLog GetFoodLogById(int foodLogId)
        {
            return _foodLogRepo.Get(foodLogId);
        }

        public FoodLog GetFoodLogByUserId(int userId)
        {
            return _foodLogRepo._table.Where(foodId => foodId.UserId == userId).FirstOrDefault();
        }

        public ErrorCode CreateFoodLog(FoodLog foodLog, ref string errMsg)
        {
            if (_foodLogRepo.Create(foodLog, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }
            return ErrorCode.Success;
        }
    }
}
