using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class UserInfo
{
    public int UserInfoId { get; set; }

    public int? Age { get; set; }

    public double? Weight { get; set; }

    public double? Height { get; set; }

    public int? UserId { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public int? BodyGoalId { get; set; }

    public virtual User? User { get; set; }
}
