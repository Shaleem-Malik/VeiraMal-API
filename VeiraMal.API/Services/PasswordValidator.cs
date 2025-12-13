using System.Text.RegularExpressions;

namespace VeiraMal.API.Services
{
    public class PasswordValidator
    {
        public (bool IsValid, string Message) ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return (false, "Password cannot be empty.");

            if (password.Length < 8)
                return (false, "Password must be at least 8 characters long.");

            // Check for at least one uppercase letter
            if (!Regex.IsMatch(password, @"[A-Z]"))
                return (false, "Password must contain at least one uppercase letter.");

            // Check for at least one lowercase letter
            if (!Regex.IsMatch(password, @"[a-z]"))
                return (false, "Password must contain at least one lowercase letter.");

            // Check for at least one digit
            if (!Regex.IsMatch(password, @"\d"))
                return (false, "Password must contain at least one number.");

            // Check for at least one special character
            if (!Regex.IsMatch(password, @"[!@#$%^&*()_+=\[{\]};:<>|./?,-]"))
                return (false, "Password must contain at least one special character.");

            return (true, "Password is valid.");
        }
    }
}