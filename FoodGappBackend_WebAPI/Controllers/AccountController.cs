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

                    return Ok(new { message = "Login successful", roleName = roleName.RoleName, userId = user.UserId });

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
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            var existingUser = _userMgr.GetUserById(UserId);
            if (existingUser == null)
            {
                return BadRequest(new { error = "User not found" });
            }

            // Only update allowed fields
            existingUser.FirstName = user.FirstName;
            existingUser.LastName = user.LastName;
            existingUser.Age = user.Age;
            existingUser.Weight = user.Weight;
            existingUser.Height = user.Height;
            existingUser.BodyGoalId = user.BodyGoalId;
            // Do NOT update Email, IsActive, or Password

            if (_userMgr.UpdateUser(existingUser, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Updating User was failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }


        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return Ok(new { message = "Logout successful" });
        }

        [HttpDelete("deleteAccount")]
        public IActionResult DeleteUserAccount(int id)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            var loginUser = _userMgr.GetUserById(UserId);
            if (loginUser == null)
            {
                return BadRequest(new { error = "User not found" });
            }

            loginUser.IsActive = false;

            if (_userMgr.UpdateUser(loginUser, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Deactivating User failed", details = ErrorMessage });
            }

            return Ok(new { message = "User deactivated successfully" });
        }

        // These two endpoints are now replaced to use User instead of UserInfo

        [HttpPost("updateUserInfo")]
        public IActionResult UpdateUserInfo([FromBody] User user)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            user.UserId = UserId;

            if (_userMgr.UpdateUser(user, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Updating User failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }

        [HttpPost("createUserInfo")]
        public IActionResult CreateUserInfo([FromBody] User user)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            user.UserId = UserId;

            if (_userMgr.CreateAccount(user, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "User info creation failed", details = ErrorMessage });
            }

            return Ok(new { message = "User created successfully" });
        }
        [HttpGet("getProfile")]
        public IActionResult GetProfile([FromQuery] int userId)
        {
            var user = _userMgr.GetUserById(userId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            return Ok(new
            {
                userId = user.UserId,
                firstName = user.FirstName,
                lastName = user.LastName,
                age = user.Age,
                weight = user.Weight,
                height = user.Height,
                bodyGoalId = user.BodyGoalId
            });
        }
    }
}
