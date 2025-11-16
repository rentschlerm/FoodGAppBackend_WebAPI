using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class User
{
    public int UserId { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public double? Weight { get; set; }

    public double? Height { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public int? BodyGoalId { get; set; }

    public bool? IsActive { get; set; }

    public int? Age { get; set; }

    public int? UserCurrentExperience { get; set; }

    public int? UserLevel { get; set; }

    public int? Gender { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual BodyGoal? BodyGoal { get; set; }

    public virtual ICollection<DailyIntake> DailyIntakes { get; set; } = new List<DailyIntake>();

    public virtual ICollection<FoodLog> FoodLogs { get; set; } = new List<FoodLog>();

    public virtual ICollection<MealPlan> MealPlans { get; set; } = new List<MealPlan>();

    public virtual ICollection<NutrientLog> NutrientLogs { get; set; } = new List<NutrientLog>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
