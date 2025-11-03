using Microsoft.AspNetCore.Http;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface INHTService
    {
        Task<string> UploadAsync(IFormFile file);
        Task<IEnumerable<NHT>> GetAllAsync();
        Task<IEnumerable<object>> GetAnalysisAsync();
        Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month);
    }
}