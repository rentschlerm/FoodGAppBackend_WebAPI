using FoodGappBackend_WebAPI.Repository;
using Microsoft.AspNetCore.Mvc;

namespace FoodGappBackend_WebAPI.Controllers
{
    [Route("api/[controller]")]
    public class FoodLoggingController : BaseController
    {
        // Interactive Food Logging transactions
        [HttpGet("createUserFoodLog")]
        public IActionResult CreateUserFoodLog()
        {
            // Logic to create a user food log
            var result = _foodLogMgr.CreateFoodLog(new Models.FoodLog { UserId = UserId }, ref ErrorMessage);

            // TEMP: Compare by string value or int instead of ErrorCode enum
            if (result.ToString().ToLower() != "success" && result.ToString() != "0")
            {
                return BadRequest(ErrorMessage);
            }

            return Ok("User food log created successfully.");
        }
    }
}
