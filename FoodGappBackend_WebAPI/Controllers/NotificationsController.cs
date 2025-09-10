using Microsoft.AspNetCore.Mvc;

namespace FoodGappBackend_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : BaseController
    {
        [HttpPost("alert")]
        public IActionResult SendAlert([FromBody] object alertData)
        {
            // TODO: Integrate with alert logic or return mock data
            return Ok(new { alert = true, message = "High sugar detected!" });
        }
    }
}
