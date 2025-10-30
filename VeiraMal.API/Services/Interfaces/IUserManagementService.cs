using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using System.IO;
using System.Threading.Tasks;

namespace VeiraMal.API.Services.Interfaces
{
    public interface IUserManagementService
    {
        Task<(User? user, string tempPassword)> CreateUserAsync(Guid companyId, CreateUserDto dto);
        Task<BulkUploadResultDto> CreateUsersFromExcelAsync(Guid companyId, Stream excelStream, string uploaderEmail);
        Task<List<User>> ListUsersAsync(Guid companyId);
        Task<User?> UpdateUserAsync(Guid companyId, UpdateUserDto dto);
        Task<bool> InactivateUserAsync(Guid companyId, int userId);
        Task<bool> ActivateUserAsync(Guid companyId, int userId);
    }
}
