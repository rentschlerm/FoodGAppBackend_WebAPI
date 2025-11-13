using Microsoft.AspNetCore.Mvc;

namespace FoodGappBackend_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommunityController : BaseController
    {
        [HttpPost("share")]
        public IActionResult ShareProgress([FromBody] object shareData)
        {
            // TODO: Integrate with social sharing or return mock data
            return Ok(new { shared = true, platform = "MockSocial" });
        }
    }
}
