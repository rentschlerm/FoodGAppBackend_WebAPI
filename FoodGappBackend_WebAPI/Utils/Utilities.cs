using BCrypt.Net;

namespace FoodGappBackend_WebAPI.Utils
{
    public class Utilities
    {
        public enum ErrorCode
        {
            Success,
            Error
        }

        // Password hashing utilities
        public static class PasswordHelper
        {
            /// <summary>
            /// Hashes a plain text password using BCrypt
            /// </summary>
            /// <param name="password">Plain text password</param>
            /// <returns>Hashed password</returns>
            public static string HashPassword(string password)
            {
                if (string.IsNullOrWhiteSpace(password))
                    throw new ArgumentException("Password cannot be null or empty");

                return BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
            }

            /// <summary>
            /// Verifies a plain text password against a hashed password
            /// </summary>
            /// <param name="password">Plain text password to verify</param>
            /// <param name="hashedPassword">Hashed password from database</param>
            /// <returns>True if password matches, false otherwise</returns>
            public static bool VerifyPassword(string password, string hashedPassword)
            {
                if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hashedPassword))
                    return false;

                try
                {
                    return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
