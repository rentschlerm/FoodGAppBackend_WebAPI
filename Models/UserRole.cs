using System;
using System.Collections.Generic;

namespace FoodGappBackend_WebAPI.Models;

public partial class UserRole
{
    public int UserRole1 { get; set; }

    public int? UserId { get; set; }

    public int? RoleId { get; set; }

    public virtual Role? Role { get; set; }

    public virtual User? User { get; set; }
}
