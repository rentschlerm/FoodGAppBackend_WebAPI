using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class BodyGoal
{
    public int BodyGoalId { get; set; }

    public string? BodyGoalName { get; set; }

    public string? BodyGoalDesc { get; set; }
}
