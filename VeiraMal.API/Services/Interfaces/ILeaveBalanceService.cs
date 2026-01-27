namespace VeiraMal.API.Services.Interfaces
{
    public interface ILeaveBalanceService
    {
        Task<string> UploadAsync(IFormFile file);
    }
}
