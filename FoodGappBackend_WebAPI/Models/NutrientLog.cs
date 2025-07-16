using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class NutrientLog
{
    public int NutrientLogId { get; set; }

    public int? FoodCategoryId { get; set; }

    public int? FoodId { get; set; }

    public string? Calories { get; set; }

    public string? Protein { get; set; }

    public string? Fat { get; set; }

    public int? UserId { get; set; }

    public double? FoodGramAmount { get; set; }

    public virtual Food? Food { get; set; }

    public virtual FoodCategory? FoodCategory { get; set; }

    public virtual User? User { get; set; }
}
