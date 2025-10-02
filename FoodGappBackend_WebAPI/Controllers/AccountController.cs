using FoodGappBackend_WebAPI.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static FoodGappBackend_WebAPI.Utils.Utilities;
using FoodGappBackend_WebAPI.Models.CustomModels;
using FoodGappBackend_WebAPI.Utils;

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
                if (string.IsNullOrWhiteSpace(ul.Email) || string.IsNullOrWhiteSpace(ul.Password))
                {
                    return BadRequest(new { error = "Email and Password are required." });
                }

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
                        new Claim(ClaimTypes.NameIdentifier, user.Email ?? string.Empty),
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
        public IActionResult Register([FromBody] BasicRegistration registration)
        {
            try
            {
                // Create user with only email and password
                var user = new User
                {
                    Email = registration.Email,
                    Password = registration.Password,
                    UserLevel = 1,
                    UserCurrentExperience = 0,
                    IsActive = true
                };

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
                        return Ok(new { message = "Registration successful", userId = user.UserId });
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

        // Add this class at the bottom of the file
        public class BasicRegistration
        {
            public required string Email { get; set; }
            public required string Password { get; set; }
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

            // Gender: expect 0 for Male, 1 for Female, 2 for Others, null or other for unspecified
            if (user.Gender == 0 || user.Gender == 1 || user.Gender == 2)
                existingUser.Gender = user.Gender;
            else
                existingUser.Gender = null;

            // Do NOT update Email, IsActive, or Password

            if (_userMgr.UpdateUser(existingUser, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Updating User was failed", details = ErrorMessage });
            }

            return Ok(new { message = "Updating User successful" });
        }

        [HttpGet("getProfile")]
        public IActionResult GetProfile([FromQuery] int userId)
        {
            var user = _userMgr.GetUserById(userId);
            if (user == null)
                return NotFound(new { error = "User not found" });

            int userLevel = user.UserLevel ?? 1;
            int userCurrentExperience = user.UserCurrentExperience ?? 0;

            return Ok(new
            {
                userInfo = new
                {
                    userId = user.UserId,
                    email = user.Email,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    age = user.Age,
                    gender = user.Gender, // returns 0 for Male, 1 for Female, 2 for Others, null for unspecified
                    weight = user.Weight,
                    height = user.Height,
                    bodyGoalId = user.BodyGoalId,
                    userLevel = user.UserLevel ?? 1,
                    userCurrentExperience = user.UserCurrentExperience ?? 0,
                    experienceToNextLevel = _userMgr.GetExperienceToNextLevel(user)
                }
            });
        }

        [HttpPost("changePassword")]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (!User.Identity?.IsAuthenticated ?? false || UserId == 0)
            {
                return BadRequest(new { error = "User is not authenticated" });
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { error = "Current password and new password are required" });
            }

            if (request.NewPassword.Length < 6)
            {
                return BadRequest(new { error = "New password must be at least 6 characters long" });
            }

            var existingUser = _userMgr.GetUserById(UserId);
            if (existingUser == null)
            {
                return BadRequest(new { error = "User not found" });
            }

            // Verify current password using hash verification
            if (!Utilities.PasswordHelper.VerifyPassword(request.CurrentPassword, existingUser.Password ?? ""))
            {
                return BadRequest(new { error = "Current password is incorrect" });
            }

            // Hash the new password before storing
            existingUser.Password = Utilities.PasswordHelper.HashPassword(request.NewPassword);

            if (_userMgr.UpdateUser(existingUser, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "Failed to update password", details = ErrorMessage });
            }

            return Ok(new { message = "Password updated successfully" });
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

            // Get the existing user that was created during registration
            var existingUser = _userMgr.GetUserById(UserId);
            if (existingUser == null)
            {
                return BadRequest(new { error = "User not found" });
            }

            // Update the existing user with profile information
            existingUser.FirstName = user.FirstName;
            existingUser.LastName = user.LastName;
            existingUser.Age = user.Age;
            existingUser.Weight = user.Weight;
            existingUser.Height = user.Height;
            existingUser.BodyGoalId = user.BodyGoalId;
            existingUser.Gender = user.Gender; // Handle gender field
            existingUser.UserLevel ??= 1;
            existingUser.UserCurrentExperience ??= 0;

            // Use UpdateUser instead of CreateAccount
            if (_userMgr.UpdateUser(existingUser, ref ErrorMessage) != ErrorCode.Success)
            {
                return BadRequest(new { error = "User profile update failed", details = ErrorMessage });
            }

            return Ok(new { message = "User profile created successfully" });
        }

        [HttpPost("add-experience")]
        public IActionResult AddExperience([FromBody] AddExperienceRequest request)
        {
            var user = _userMgr.GetUserById(request.UserId);
            if (user == null)
                return NotFound();

            _userMgr.AddExperience(user, request.Experience);
            var result = _userMgr.UpdateUser(user, ref ErrorMessage);

            if (result != ErrorCode.Success)
                return BadRequest(new { error = ErrorMessage });

            return Ok(new
            {
                userId = user.UserId,
                userLevel = user.UserLevel,
                userCurrentExperience = user.UserCurrentExperience,
                experienceToNextLevel = _userMgr.GetExperienceToNextLevel(user)
            });
        }

        [HttpGet("badges")]
        public IActionResult GetBadges(int userId)
        {
            // TODO: Integrate with gamification logic or return mock data
            return Ok(new[] { new { badge = "Consistent Logger", earned = true, date = DateTime.Now } });
        }

        [HttpPost("log-cebu-food")]
        public IActionResult LogCebuFood([FromBody] object foodData)
        {
            // TODO: Integrate with Cebu food logic or return mock data
            return Ok(new { logged = true, dish = "Adobo", culturalRelevance = true });
        }

        [HttpGet("user-level/{userId}")]
        public IActionResult GetUserLevel(int userId)
        {
            var user = _userMgr.GetUserById(userId);
            if (user == null)
                return NotFound();

            return Ok(new
            {
                level = user.UserLevel ?? 1,
                currentXP = user.UserCurrentExperience ?? 0,
                requiredXP = _userMgr.GetExperienceToNextLevel(user),
                title = $"Level {user.UserLevel ?? 1}",
                badge = "" // optional, you can add logic for badges here
            });
        }
    }

    public class AddExperienceRequest
    {
        public int UserId { get; set; }
        public int Experience { get; set; }
    }

    public class ChangePasswordRequest
    {
        public required string CurrentPassword { get; set; }
        public required string NewPassword { get; set; }
    }
}