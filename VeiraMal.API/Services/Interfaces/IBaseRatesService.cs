using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface IBaseRatesService
    {
        Task<string> EnsureBaseRatesAsync(IFormFile? file = null, RateUnit defaultUnit = RateUnit.Hourly);
    }
}
