using FoodGappBackend_WebAPI.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static FoodGappBackend_WebAPI.Utils.Utilities;
using FoodGappBackend_WebAPI.Models.CustomModels;

namespace FoodGappBackend_WebAPI.Controllers
{
    [Route("api/[controller]")]
    public class AccountController : BaseController
    {
        private IActionResult? CheckAuthentication()
        {
            if (!User.Identity?.IsAuthenticated ?? false)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            return null;
        }

        IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;
        public AccountController(IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] CustomUserLogin ul)
        {
            try
            {
                if (_userMgr.SignIn(ul.Email, ul.Password, ref ErrorMessage) == ErrorCode.Success)
                {
                    var user = _userMgr.GetUserByEmail(ul.Email);
                    if (user == null)
                    {
                        return BadRequest(new { error = "User not found." });
                    }

                    var userRole = _userMgr.GetUsersRoleByUserId(user.UserId);
                    if (userRole == null)
                    {
                        return BadRequest(new { error = "UserRole not found." });
                    }

                    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Email),
                        new Claim(ClaimTypes.Name, user.UserId.ToString()),
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var properties = new AuthenticationProperties
                    {
                        AllowRefresh = true,
                        IsPersistent = true,
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity),
                        properties
                    );

                    var roleName = _userMgr.GetRoleNameByRoleId(userRole.RoleId);

                    return Ok(new { message = "Login successful", roleName = roleName.RoleName });

                }

                return BadRequest(new { error = "Invalid login credentials" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during login: {ex.Message}");
                return StatusCode(500, new { error = "An unexpected error occurred. Please try again later." });
            }
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] User user)
        {
            try
            {
                if (_userMgr.CreateAccount(user, ref ErrorMessage) == ErrorCode.Success)
                {
                    var role = _roleRepo.GetAll().FirstOrDefault(r => r.RoleName == "User");

                    if (role == null)
                    {
                        return BadRequest(new { error = "Role 'User' not found" });
                    }

                    var userRole = new UserRole
                    {
                        UserId = user.UserId,
                        RoleId = role.RoleId
                    };

                    if (_userRoleRepo.Create(userRole, out ErrorMessage) == ErrorCode.Success)
                    {
                        return Ok(new { message = "Registration successful" });
                    }

                    return BadRequest(new { error = "Failed to assign user role", details = ErrorMessage });
                }

                return BadRequest(new { error = "Account registration failed", details = ErrorMessage });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An unexpected error occurred", details = ex.Message });
            }
        }

        [HttpPost("updateAccount")]
        public IActionResult UpdateUserAccount([FromBody] User user)
        {
            CheckAuthentication();

            user.UserId = UserId;

            if (_userMgr.UpdateUser(user, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Updating User was failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }

        [HttpGet("userInfo")]
        public JsonResult UserInfo()
        {
            CheckAuthentication();

            var userInfo = _userMgr.GetUserInfoByUserId(UserId);

            if (userInfo == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    userInfo.Age,
                    userInfo.Weight,
                    userInfo.FirstName,
                    userInfo.LastName,
                }
            });
        }

        [HttpDelete("deleteAccount")]
        public IActionResult DeleteUserAccount(int id)
        {

            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            var loginUser = _userMgr.GetUserById(UserId);

            if (_userMgr.DeleteUser(loginUser.UserId, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Deleting User was failed", details = ErrorMessage });
            }

            return Ok(new { message = "Deleting User successful" });
        }

        [HttpPost("updateUserInfo")]
        public IActionResult UpdateUserInfo([FromBody] UserInfo userInfo)
        {
            CheckAuthentication();

            userInfo.UserId = UserId;

            if (_userMgr.UpdateUserInfo(userInfo, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Updating User failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }

        [HttpPost("createUserInfo")]
        public IActionResult CreateUserInfo([FromBody] UserInfo userInfo)
        {
            CheckAuthentication();

            userInfo.UserId = UserId;

            if (_userMgr.CreateUserInfo(userInfo, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "User info updated failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }
    }
}
