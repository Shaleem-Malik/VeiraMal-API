namespace VeiraMal.API.Services.Interfaces
{
    public interface ILiabilityService
    {
        Task<string> CalculateLiabilitiesAsync();
    }
}