using Microsoft.AspNetCore.Http;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface IHeadcountService
    {
        Task<string> UploadAsync(IFormFile file);
        Task<IEnumerable<Headcount>> GetAllAsync();
        Task<IEnumerable<object>> GetAnalysisAsync();

        // Accept organizationalKey so the client can request analysis for any org key (not just Finance).
        Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month, string organizationalKey = "Finance");
    }
}
