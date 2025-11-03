// Services/Interfaces/ISubCompanyResolver.cs
using System;
using System.Threading.Tasks;

namespace VeiraMal.API.Services.Interfaces
{
    public interface ISubCompanyResolver
    {
        /// <summary>
        /// Resolve which CompanyId the caller should operate as.
        /// Throws UnauthorizedAccessException when caller is not allowed to act on requested subCompanyId.
        /// Throws InvalidOperationException when caller has multiple assignments and no explicit query was provided.
        /// Returns the resolved CompanyId (either baseCompanyId or an assigned subCompanyId).
        /// </summary>
        Task<Guid> ResolveTargetCompanyIdAsync(Guid baseCompanyId, int callerUserId, Guid? querySubCompanyId);
    }
}
