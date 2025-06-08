using FoodGappBackend_WebAPI.Data;
using FoodGappBackend_WebAPI.Models;
using FoodGappBackend_WebAPI.Repository;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FoodGappBackend_WebAPI.Controllers
{
    public class BaseController : Controller
    {
        public String ErrorMessage;
        public FoodGappDbContext _db;
        public UserManager _userMgr;
        public BaseRepository<Role> _roleRepo;
        public BaseRepository<UserRole> _userRoleRepo;
        public FoodLoggingManager _foodLogMgr;

        public int UserId { get { var userId = Convert.ToInt32(User.FindFirst(ClaimsIdentity.DefaultNameClaimType)?.Value); return userId; } }

        public BaseController()
        {
            ErrorMessage = String.Empty;
            _db = new FoodGappDbContext();
            _userMgr = new UserManager();
            _roleRepo = new BaseRepository<Role>();
            _userRoleRepo = new BaseRepository<UserRole>();
            _foodLogMgr = new FoodLoggingManager();
        }
    }
}
