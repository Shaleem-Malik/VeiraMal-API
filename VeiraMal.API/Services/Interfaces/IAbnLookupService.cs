using VeiraMal.API.DTOs;

namespace VeiraMal.API.Services.Interfaces
{
    public interface IAbnLookupService
    {
        Task<AbnValidationResultDto> ValidateAbnAsync(string abn, CancellationToken cancellationToken = default);
    }
}
