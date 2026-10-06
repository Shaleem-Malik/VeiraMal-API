using Microsoft.AspNetCore.Http;
using VeiraMal.API.DTOs;

namespace VeiraMal.API.Services.Interfaces;

public interface IHrAnalyticsService
{
    Task<HrAnalyticsSettingsDto> GetSettingsAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<HrAnalyticsSettingsDto> UpdateSettingsAsync(Guid companyId, HrAnalyticsSettingsUpdateDto request, CancellationToken cancellationToken = default);

    Task<HrDashboardDto> GetDashboardAsync(Guid companyId, DateTime reportingDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HrEmployeeSearchDto>> SearchEmployeesAsync(Guid companyId, string query, CancellationToken cancellationToken = default);
    Task<HrImportResultDto> ImportWorkbookAsync(Guid companyId, int uploadedByUserId, IFormFile file, CancellationToken cancellationToken = default);
}
