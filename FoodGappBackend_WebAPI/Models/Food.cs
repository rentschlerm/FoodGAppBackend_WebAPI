using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class Food
{
    public string? FoodName { get; set; }

    public int FoodId { get; set; }

    public int? FoodCategoryId { get; set; }

    public virtual FoodCategory? FoodCategory { get; set; }

    public virtual ICollection<FoodLog> FoodLogs { get; set; } = new List<FoodLog>();

    public virtual ICollection<NutrientLog> NutrientLogs { get; set; } = new List<NutrientLog>();
}
