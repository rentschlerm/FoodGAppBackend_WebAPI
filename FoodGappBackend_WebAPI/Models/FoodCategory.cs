using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class FoodCategory
{
    public int FoodCategoryId { get; set; }

    public string? FoodCategoryName { get; set; }

    public virtual ICollection<FoodLog> FoodLogs { get; set; } = new List<FoodLog>();

    public virtual ICollection<Food> Foods { get; set; } = new List<Food>();

    public virtual ICollection<NutrientLog> NutrientLogs { get; set; } = new List<NutrientLog>();
}
