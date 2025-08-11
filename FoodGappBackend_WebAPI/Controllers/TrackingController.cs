using Microsoft.AspNetCore.Mvc;
using FoodGappBackend_WebAPI.Models;
using FoodGappBackend_WebAPI.Repository;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrackingController : ControllerBase
    {
        private readonly TrackingManager _trackingManager = new TrackingManager();

        [HttpGet("daily-intake")]
        public IActionResult GetTodaysIntake(int userId)
        {
            var today = DateTime.Now.Date;
            var intake = _trackingManager.GetTodaysIntake(userId, today);
            if (intake == null)
                return NotFound();
            return Ok(intake);
        }

        [HttpPost("reset-daily-intake")]
        public IActionResult ResetDailyIntake(int userId)
        {
            var today = DateTime.Now.Date;
            var result = _trackingManager.ResetDailyIntake(userId, today);
            return Ok(new { success = result });
        }

        [HttpPost("add-calories")]
        public IActionResult AddCaloriesToToday(int userId, int calories)
        {
            var today = DateTime.Now.Date;
            var result = _trackingManager.AddCaloriesToToday(userId, calories, today);
            return Ok(new { success = result });
        }

        [HttpGet("nutrient-log")]
        public IActionResult GetNutrientLogById(int nutrientLogId)
        {
            var log = _trackingManager.GetNutrientLogById(nutrientLogId);
            if (log == null)
                return NotFound();
            return Ok(log);
        }

        [HttpPost("nutrient-log")]
        public IActionResult CreateNutrientLog([FromBody] NutrientLog log)
        {
            string errMsg = "";
            var result = _trackingManager.CreateNutrientLog(log, ref errMsg);
            return Ok(new { success = result == ErrorCode.Success, error = errMsg });
        }
    }
}