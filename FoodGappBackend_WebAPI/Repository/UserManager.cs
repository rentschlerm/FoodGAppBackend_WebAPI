using FoodGappBackend_WebAPI.Models;
using static FoodGappBackend_WebAPI.Utils.Utilities;

namespace FoodGappBackend_WebAPI.Repository
{
    public class UserManager
    {
        private readonly BaseRepository<User> _userRepo;
        private readonly BaseRepository<Role> _role;
        private readonly BaseRepository<UserRole> _userRole;
        private readonly BaseRepository<UserInfo> _userInfo;

        public UserManager()
        {
            _userRepo = new BaseRepository<User>();
            _role = new BaseRepository<Role>();
            _userRole = new BaseRepository<UserRole>();
            _userInfo = new BaseRepository<UserInfo>();
        }

        public User GetUserById(int userId)
        {
            return _userRepo.Get(userId);
        }

        public UserInfo GetUserInfoByUserId(int userId)
        {
            return _userInfo._table.Where(ur => ur.UserId == userId).FirstOrDefault();
        }

        public UserInfo GetUserInfoById(int id)
        {
            return _userInfo.Get(id);
        }

        public UserRole GetUsersRoleByUserId(int userId)
        {
            return _userRole._table.Where(ur => ur.UserId == userId).FirstOrDefault();
        }

        public Role GetRoleNameByRoleId(int? roleId)
        {
            return _role._table.Where(r => r.RoleId == roleId).FirstOrDefault();
        }

        public User GetUserByEmail(string email)
        {
            return _userRepo._table.Where(e => e.Email == email).FirstOrDefault();
        }

        public ErrorCode SignIn(string email, string password, ref string errMsg)
        {
            var userSignIn = GetUserByEmail(email);
            if (userSignIn == null)
            {
                errMsg = "Invalid username or password.";
                return ErrorCode.Error;
            }

            if (!userSignIn.Password.Equals(password))
            {
                errMsg = "Invalid username or password.";
                return ErrorCode.Error;
            }

            errMsg = "Login Successfulss";
            return ErrorCode.Success;
        }

        public ErrorCode CreateAccount(User u, ref string errMsg)
        {
            if (GetUserByEmail(u.Email) != null)
            {
                errMsg = "Username Already Exist";
                return ErrorCode.Error;
            }

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

        public ErrorCode CreateUserInfo(UserInfo ui, ref string errMsg)
        {
            if (_userInfo.Create(ui, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }

            return ErrorCode.Success;
        }

        public ErrorCode UpdateUserInfo(UserInfo u, ref string errMsg)
        {
            var currentUserInfo = GetUserInfoByUserId(u.UserId.Value);

            if (currentUserInfo == null)
            {
                errMsg = "UserId cannot be null.";
                return ErrorCode.Error;
            }

            currentUserInfo.Age = u.Age;
            currentUserInfo.FirstName = u.FirstName;
            currentUserInfo.LastName = u.LastName;
            currentUserInfo.Weight = u.Weight;
            currentUserInfo.Height = u.Height;

            if (_userInfo.Update(currentUserInfo.UserInfoId, currentUserInfo, out errMsg) != ErrorCode.Success)
            {
                return ErrorCode.Error;
            }
            return ErrorCode.Success;
        }

    }
}
