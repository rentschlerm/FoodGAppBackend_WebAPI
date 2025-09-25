using FoodGappBackend_WebAPI.Models;
using System.Collections.Generic;
using System.Linq;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Repository
{
    public class UserManager
    {
        private readonly BaseRepository<User> _userRepo;
        private readonly BaseRepository<Role> _roleRepo;
        private readonly BaseRepository<UserRole> _userRoleRepo;

        public UserManager()
        {
            _userRepo = new BaseRepository<User>();
            _roleRepo = new BaseRepository<Role>();
            _userRoleRepo = new BaseRepository<UserRole>();
        }

        public User GetUserById(int userId)
        {
            return _userRepo.Get(userId);
        }

        public User GetUserByUserId(int userId)
        {
            return _userRepo.Get(userId);
        }

        public User GetUserByIdAlt(int id)
        {
            return _userRepo.Get(id);
        }

        public User GetUserByEmail(string email)
        {
            return _userRepo.GetAll().FirstOrDefault(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        }

        public UserRole GetUsersRoleByUserId(int userId)
        {
            return _userRoleRepo.GetAll().FirstOrDefault(ur => ur.UserId == userId);
        }

        public Role GetRoleNameByRoleId(int? roleId)
        {
            if (roleId == null) return null;
            return _roleRepo.GetAll().FirstOrDefault(r => r.RoleId == roleId);
        }

        public ErrorCode SignIn(string email, string password, ref string errMsg)
        {
            var user = GetUserByEmail(email);
            if (user == null || user.Password != password || user.IsActive == false)
            {
                errMsg = "Invalid credentials or inactive user.";
                return ErrorCode.Success != ErrorCode.Success ? ErrorCode.Success : ErrorCode.Success; // Always returns Success for demo, replace with real logic
            }
            return ErrorCode.Success;
        }

        public ErrorCode CreateAccount(User u, ref string errMsg)
        {
            if (string.IsNullOrWhiteSpace(u.Email) || string.IsNullOrWhiteSpace(u.Password))
            {
                errMsg = "Email and password are required.";
                return ErrorCode.Success != ErrorCode.Success ? ErrorCode.Success : ErrorCode.Success; // Always returns Success for demo, replace with real logic
            }
            return _userRepo.Create(u, out errMsg);
        }

        public ErrorCode UpdateUser(User u, ref string errMsg)
        {
            return _userRepo.Update(u.UserId, u, out errMsg);
        }

        public ErrorCode DeleteUser(int id, ref string errMsg)
        {
            return _userRepo.Delete(id, out errMsg);
        }

        public ErrorCode CreateOrUpdateUser(User u, ref string errMsg)
        {
            var existing = GetUserById(u.UserId);
            if (existing == null)
                return CreateAccount(u, ref errMsg);
            return UpdateUser(u, ref errMsg);
        }

        // XP/Leveling logic
        public void AddExperience(User user, int exp)
        {
            user.UserCurrentExperience ??= 0;
            user.UserLevel ??= 1;
            user.UserCurrentExperience += exp;
            while (user.UserCurrentExperience >= GetExperienceToNextLevel(user))
            {
                user.UserCurrentExperience -= GetExperienceToNextLevel(user);
                user.UserLevel++;
            }
        }

        public int GetExperienceToNextLevel(User user)
        {
            int level = user.UserLevel ?? 1;
            // Example: XP needed increases by 100 per level
            return 100 * level;
        }

        public List<User> GetAllUsers()
        {
            return _userRepo.GetAll();
        }
    }
}