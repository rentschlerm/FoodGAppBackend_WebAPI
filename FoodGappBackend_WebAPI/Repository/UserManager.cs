using FoodGappBackend_WebAPI.Models;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Repository
{
    public class UserManager
    {
        private readonly BaseRepository<User> _userRepo;
        private readonly BaseRepository<Role> _role;
        private readonly BaseRepository<UserRole> _userRole;

        public UserManager()
        {
            _userRepo = new BaseRepository<User>();
            _role = new BaseRepository<Role>();
            _userRole = new BaseRepository<UserRole>();
        }

        public User GetUserById(int userId)
        {
            return _userRepo.Get(userId);
        }

        // Replaces GetUserInfoByUserId
        public User GetUserByUserId(int userId)
        {
            return _userRepo._table.FirstOrDefault(u => u.UserId == userId);
        }

        // Replaces GetUserInfoById
        public User GetUserByIdAlt(int id)
        {
            return _userRepo.Get(id);
        }

        public UserRole GetUsersRoleByUserId(int userId)
        {
            return _userRole._table.FirstOrDefault(ur => ur.UserId == userId);
        }

        public Role GetRoleNameByRoleId(int? roleId)
        {
            return _role._table.FirstOrDefault(r => r.RoleId == roleId);
        }

        public User GetUserByEmail(string email)
        {
            return _userRepo._table.FirstOrDefault(e => e.Email == email);
        }

        public ErrorCode SignIn(string email, string password, ref string errMsg)
        {
            var userSignIn = GetUserByEmail(email);
            if (userSignIn == null || userSignIn.IsActive == false)
            {
                errMsg = "User not found or deactivated.";
                return ErrorCode.Error;
            }

            // Use BCrypt to verify the password
            if (!BCrypt.Net.BCrypt.Verify(password, userSignIn.Password))
            {
                errMsg = "Invalid username or password.";
                return ErrorCode.Error;
            }

            errMsg = "Login Successful";
            return ErrorCode.Success;
        }

        public ErrorCode CreateAccount(User u, ref string errMsg)
        {
            if (GetUserByEmail(u.Email) != null)
            {
                errMsg = "Username Already Exist";
                return ErrorCode.Error;
            }

            // Hash the password before saving
            u.Password = BCrypt.Net.BCrypt.HashPassword(u.Password);

            if (_userRepo.Create(u, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }

            return ErrorCode.Success;
        }

        public ErrorCode UpdateUser(User u, ref string errMsg)
        {
            if (_userRepo.Update(u.UserId, u, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }
            return ErrorCode.Success;
        }

        public ErrorCode DeleteUser(int id, ref string errMsg)
        {
            if (_userRepo.Delete(id, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }
            return ErrorCode.Success;
        }

        // Replaces CreateUserInfo
        public ErrorCode CreateOrUpdateUser(User u, ref string errMsg)
        {
            var existingUser = GetUserById(u.UserId);
            if (existingUser == null)
            {
                // Create new user
                if (_userRepo.Create(u, out errMsg) != ErrorCode.Success)
                {
                    return ErrorCode.Error;
                }
            }
            else
            {
                // Update existing user
                existingUser.Age = u.Age;
                existingUser.FirstName = u.FirstName;
                existingUser.LastName = u.LastName;
                existingUser.Weight = u.Weight;
                existingUser.Height = u.Height;
                existingUser.Email = u.Email;
                existingUser.Password = u.Password;
                existingUser.BodyGoalId = u.BodyGoalId;
                existingUser.IsActive = u.IsActive;

                if (_userRepo.Update(existingUser.UserId, existingUser, out errMsg) != ErrorCode.Success)
                {
                    return ErrorCode.Error;
                }
            }
            return ErrorCode.Success;
        }
    }
}
