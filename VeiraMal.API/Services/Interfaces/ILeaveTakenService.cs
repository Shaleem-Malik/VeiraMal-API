namespace VeiraMal.API.Services.Interfaces
{
    public interface ILeaveTakenService
    {
        Task<string> UploadAsync(IFormFile file);
    }
}
