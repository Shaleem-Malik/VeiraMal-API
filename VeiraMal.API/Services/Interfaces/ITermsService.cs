using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface ITermsService
    {
        Task<int> UploadTermsExcelAsync(IFormFile file);
        Task<IEnumerable<Terms>> GetAllTermsAsync();
        Task<object> GetTurnoverAnalysisAsync();
        Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month);
    }
}
