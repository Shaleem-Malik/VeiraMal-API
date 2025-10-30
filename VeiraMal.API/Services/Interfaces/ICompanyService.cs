using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services.Interfaces
{
    public interface ICompanyService
    {
        // -------------------- Existing --------------------

        /// <summary>
        /// Onboards a company and creates its primary superuser.
        /// </summary>
        Task<Guid> OnboardCompanyAsync(CompanyOnboardDto dto, string signinLinkBase);

        /// <summary>
        /// Fetch company details for viewing.
        /// </summary>
        Task<CompanyDto?> GetCompanyAsync(Guid companyId);

        /// <summary>
        /// Update company details.
        /// </summary>
        Task<CompanyDto> UpdateCompanyAsync(Guid companyId, CompanyUpdateDto dto);


        // -------------------- New for Subcompanies --------------------

        /// <summary>
        /// Creates a subcompany under a given parent company.
        /// </summary>
        Task<SubCompanyDto> CreateSubCompanyAsync(Guid parentCompanyId, CreateSubCompanyDto dto);

        /// <summary>
        /// Lists all subcompanies for a given parent company.
        /// </summary>
        Task<List<SubCompanyDto>> ListSubCompaniesAsync(Guid parentCompanyId);

        /// <summary>
        /// Returns all superusers belonging to the parent company.
        /// </summary>
        Task<List<User>> GetParentCompanySuperUsersAsync(Guid parentCompanyId);

        /// <summary>
        /// Assigns selected parent-company superusers to a subcompany.
        /// </summary>
        Task AssignSuperUsersToSubCompanyAsync(Guid parentCompanyId, Guid subCompanyId, int[] userIds, bool replaceExisting = true);
    }
}
