// Services/SubCompanyResolver.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.Models;

namespace VeiraMal.API.Services
{
    public class SubCompanyResolver : ISubCompanyResolver
    {
        private readonly AppDbContext _db;
        private readonly ILogger<SubCompanyResolver> _logger;

        public SubCompanyResolver(AppDbContext db, ILogger<SubCompanyResolver> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Guid> ResolveTargetCompanyIdAsync(Guid baseCompanyId, int callerUserId, Guid? querySubCompanyId)
        {
            // If client explicitly requested a target
            if (querySubCompanyId.HasValue)
            {
                var requested = querySubCompanyId.Value;

                // If requested equals base company -> allow (caller operates on parent company)
                if (requested == baseCompanyId)
                {
                    _logger.LogDebug("ResolveTargetCompanyId: requested == baseCompanyId -> using base {BaseCompany}", baseCompanyId);
                    return baseCompanyId;
                }

                // Validate requested subcompany exists and belongs to the base company
                var subcompany = await _db.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CompanyId == requested);

                if (subcompany == null)
                {
                    _logger.LogWarning("ResolveTargetCompanyId: requested company {Requested} not found", requested);
                    throw new UnauthorizedAccessException("Requested subcompany not found or not accessible.");
                }

                if (subcompany.ParentCompanyId != baseCompanyId)
                {
                    _logger.LogWarning("ResolveTargetCompanyId: requested company {Requested} does not belong to base {Base}", requested, baseCompanyId);
                    throw new UnauthorizedAccessException("Requested subcompany does not belong to your parent company.");
                }

                // Validate assignment: caller must be assigned to that subcompany
                var assigned = await _db.CompanySuperUserAssignments
                    .AsNoTracking()
                    .AnyAsync(a => a.CompanyId == requested && a.UserId == callerUserId);

                if (!assigned)
                {
                    _logger.LogWarning("ResolveTargetCompanyId: caller {UserId} not assigned to subcompany {Requested}", callerUserId, requested);
                    throw new UnauthorizedAccessException("Caller is not assigned to the requested subcompany.");
                }

                _logger.LogDebug("ResolveTargetCompanyId: explicit requested subcompany {Requested} allowed for caller {UserId}", requested, callerUserId);
                return requested;
            }

            // No explicit request -> list assignments for the caller but only those that belong to this base company
            var assignments = await _db.CompanySuperUserAssignments
                .AsNoTracking()
                .Where(a => a.UserId == callerUserId)
                .Join(_db.Companies.AsNoTracking(),
                      a => a.CompanyId,
                      c => c.CompanyId,
                      (a, c) => c)
                .Where(c => c.ParentCompanyId == baseCompanyId) // important filter
                .Select(c => c.CompanyId)
                .Distinct()
                .ToListAsync();

            _logger.LogDebug("ResolveTargetCompanyId: caller {UserId} has {Count} assignments for base {Base}", callerUserId, assignments.Count, baseCompanyId);

            if (assignments.Count == 0)
            {
                _logger.LogDebug("ResolveTargetCompanyId: no assignments -> using base {Base}", baseCompanyId);
                return baseCompanyId;
            }
            else if (assignments.Count == 1)
            {
                _logger.LogDebug("ResolveTargetCompanyId: single assignment -> using subcompany {Sub}", assignments[0]);
                return assignments[0];
            }
            else
            {
                _logger.LogWarning("ResolveTargetCompanyId: caller {UserId} has multiple assignments under base {Base}", callerUserId, baseCompanyId);
                throw new InvalidOperationException("Caller has multiple subcompany assignments: specify subCompanyId query parameter.");
            }
        }
    }
}
