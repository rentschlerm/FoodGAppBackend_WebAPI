using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class DailyIntake
{
    public int DailyIntakeId { get; set; }

    public int? UserId { get; set; }

    public int? CalorieIntake { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User? User { get; set; }
}
