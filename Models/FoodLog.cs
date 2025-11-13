using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class FoodLog
{
    public int FoodLogId { get; set; }

    public int? UserId { get; set; }

    public int? FoodCategoryId { get; set; }

    public int? FoodId { get; set; }

    public int? NutrientLogId { get; set; }

    public virtual Food? Food { get; set; }

    public virtual FoodCategory? FoodCategory { get; set; }

    public virtual User? User { get; set; }
}
