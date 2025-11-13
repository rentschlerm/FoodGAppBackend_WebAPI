using System.ComponentModel.DataAnnotations;

namespace FoodGappBackend_WebAPI.Models.CustomModels
{
    public class CustomUserLogin
    {
        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(100)]
        public string? Password { get; set; }
    }
}
