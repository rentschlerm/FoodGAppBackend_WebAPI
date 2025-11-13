using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class MealPlan
{
    public int MealPlanId { get; set; }

    public int? UserId { get; set; }

    public DateTime? Date { get; set; }

    public string? MealType { get; set; }

    public string? Recipe { get; set; }

    public int? NutrientLogId { get; set; }

    public string? MicroNutrients { get; set; }

    public string? AlertNotes { get; set; }

    public virtual User? User { get; set; }
}
