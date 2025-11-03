using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<User> _hasher;

        public UserService(AppDbContext db, IPasswordHasher<User> hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _db.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> VerifyPasswordAsync(User user, string password)
        {
            if (user?.PasswordHash == null)
                return false;

            var res = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return res == PasswordVerificationResult.Success ||
                   res == PasswordVerificationResult.SuccessRehashNeeded;
        }

        public Task<string> GenerateTemporaryPasswordAsync()
        {
            // Create 12 char secure password with uppercase, lowercase, digits, special chars
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string special = "!@#$%^&*()-_=+[]{};:,.<>?";
            var all = upper + lower + digits + special;

            var pw = new StringBuilder();
            using var rng = RandomNumberGenerator.Create();

            // ensure each set represented
            pw.Append(upper[GetRandomInt(rng, upper.Length)]);
            pw.Append(lower[GetRandomInt(rng, lower.Length)]);
            pw.Append(digits[GetRandomInt(rng, digits.Length)]);
            pw.Append(special[GetRandomInt(rng, special.Length)]);

            for (int i = 4; i < 12; i++)
                pw.Append(all[GetRandomInt(rng, all.Length)]);

            // shuffle
            var arr = pw.ToString().ToCharArray();
            Shuffle(rng, arr);

            return Task.FromResult(new string(arr));
        }

        public Task SetPasswordHashAsync(User user, string password)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
            return Task.CompletedTask;
        }

        private static int GetRandomInt(RandomNumberGenerator rng, int max)
        {
            var uint32Buffer = new byte[4];
            rng.GetBytes(uint32Buffer);
            var value = BitConverter.ToUInt32(uint32Buffer, 0);
            return (int)(value % max);
        }

        private static void Shuffle(RandomNumberGenerator rng, char[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = GetRandomInt(rng, i + 1);
                var tmp = array[i];
                array[i] = array[j];
                array[j] = tmp;
            }
        }
    }
}