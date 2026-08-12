using System.Threading.Tasks;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface IUserService
    {
        Task<User?> GetByEmailAsync(string email);
        Task<bool> VerifyPasswordAsync(User user, string password);
        Task<string> GenerateTemporaryPasswordAsync();
        Task SetPasswordHashAsync(User user, string password);
        Task<bool> HasPasswordBeenUsedAsync(User user, string newPassword, int historyLimit = 5);
        Task RotatePasswordAsync(User user, string newPassword, int historyLimit = 5);
    }
}