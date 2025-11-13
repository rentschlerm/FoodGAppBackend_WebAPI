using Microsoft.AspNetCore.Mvc;

namespace FoodGappBackend_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIDietRecommenderController : BaseController
    {
        [HttpPost("recommend")]
        public IActionResult RecommendDiet([FromBody] object userData)
        {
            // TODO: Integrate with AI API or return mock data
            return Ok(new { suggestions = new[] { "Eat more vegetables", "Reduce sugar" }, goal = "weight loss" });
        }
    }
}
