using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Properties.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompanyController : ControllerBase
    {
        private readonly ICompanyService _companyService;
        private readonly ILogger<CompanyController> _logger;
        private readonly ISubCompanyResolver _subCompanyResolver;
        private readonly AppDbContext _db;

        public CompanyController(
            ICompanyService companyService,
            ISubCompanyResolver subCompanyResolver,
            AppDbContext db,
            ILogger<CompanyController> logger)
        {
            _companyService = companyService;
            _subCompanyResolver = subCompanyResolver;
            _db = db;
            _logger = logger;
        }

        [HttpPost("onboard")]
        public async Task<IActionResult> Onboard([FromBody] CompanyOnboardDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var signinBase = Request.Headers.ContainsKey("Origin")
                ? Request.Headers["Origin"].ToString() + "/signin"
                : $"{Request.Scheme}://{Request.Host.Value}/signin";

            try
            {
                var companyId = await _companyService.OnboardCompanyAsync(dto, signinBase);
                return Ok(new { CompanyId = companyId, Message = "Company onboarded and superuser invited." });
            }
            catch (ArgumentException aex)
            {
                // validation error (e.g., ABN)
                return BadRequest(new { Message = aex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while onboarding company");
                return StatusCode(500, new { Message = "An unexpected error occurred during onboarding." });
            }
        }

        // ----- NEW: Get company details
        [HttpGet("effective")]
        [Authorize]
        public async Task<IActionResult> GetEffectiveCompany([FromQuery] Guid? subCompanyId)
        {
            try
            {
                var baseCompanyClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                var callerUserClaim = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;

                if (string.IsNullOrEmpty(baseCompanyClaim) || string.IsNullOrEmpty(callerUserClaim))
                {
                    return Forbid();
                }

                var baseCompanyId = Guid.Parse(baseCompanyClaim);
                var callerUserId = int.Parse(callerUserClaim);

                var targetCompanyId = await _subCompanyResolver.ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);

                var company = await _companyService.GetCompanyAsync(targetCompanyId);
                if (company == null) return NotFound(new { Message = "Company not found." });

                return Ok(company);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching effective company");
                return StatusCode(500, new { Message = "An error occurred fetching the company." });
            }
        }

        // ----- NEW: Update company details
        [HttpPut("effective")]
        [Authorize]
        public async Task<IActionResult> UpdateEffectiveCompany([FromQuery] Guid? subCompanyId, [FromBody] CompanyUpdateDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                // read claims
                var baseCompanyClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                var callerUserClaim = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
                if (string.IsNullOrEmpty(baseCompanyClaim) || string.IsNullOrEmpty(callerUserClaim))
                    return Forbid();

                var baseCompanyId = Guid.Parse(baseCompanyClaim);
                var callerUserId = int.Parse(callerUserClaim);

                // resolve target (can be baseCompanyId or a subcompany assigned to this caller)
                var targetCompanyId = await _subCompanyResolver.ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);

                // Permission check: ensure caller is a superUser of the base (parent) company
                // (This matches your assignment model: only parent-company superusers can manage company-level data.)
                var caller = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == callerUserId && u.CompanyId == baseCompanyId);

                if (caller == null || !string.Equals(caller.AccessLevel, "superUser", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("UpdateEffectiveCompany: caller {UserId} is not superUser of base {BaseCompanyId}", callerUserId, baseCompanyId);
                    return Forbid();
                }

                // Perform update via existing service (it validates ABN and returns updated dto)
                var updated = await _companyService.UpdateCompanyAsync(targetCompanyId, dto);

                return Ok(new { Message = "Company updated successfully.", Company = updated });
            }
            catch (ArgumentException aex)
            {
                // validation error from service (e.g., ABN format)
                return BadRequest(new { Message = aex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating effective company");
                return StatusCode(500, new { Message = "An error occurred updating the company." });
            }
        }

        // POST: /api/companies/{parentCompanyId}/subcompanies
        [HttpPost("{parentCompanyId:guid}/subcompanies")]
        public async Task<IActionResult> CreateSubCompany(Guid parentCompanyId, [FromBody] CreateSubCompanyDto dto)
        {
            try
            {
                // Authorization: ensure caller is a superuser of parentCompanyId (you should hook in your auth)
                // Example: if (!UserIsSuperUserForCompany(parentCompanyId)) return Forbid();

                var sub = await _companyService.CreateSubCompanyAsync(parentCompanyId, dto);
                return CreatedAtAction(
                    nameof(GetSubCompany),
                    new { parentCompanyId = parentCompanyId, subCompanyId = sub.CompanyId },
                    sub);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET: /api/companies/{parentCompanyId}/subcompanies
        [HttpGet("{parentCompanyId:guid}/subcompanies")]
        public async Task<IActionResult> ListSubCompanies(Guid parentCompanyId)
        {
            var list = await _companyService.ListSubCompaniesAsync(parentCompanyId);
            return Ok(list);
        }

        // GET: /api/companies/{parentCompanyId}/subcompanies/{subCompanyId}
        [HttpGet("{parentCompanyId:guid}/subcompanies/{subCompanyId:guid}")]
        public async Task<IActionResult> GetSubCompany(Guid parentCompanyId, Guid subCompanyId)
        {
            var subList = await _companyService.ListSubCompaniesAsync(parentCompanyId);
            var sub = subList.FirstOrDefault(s => s.CompanyId == subCompanyId);

            if (sub == null)
                return NotFound();

            return Ok(sub);
        }

        // GET: /api/companies/{parentCompanyId}/superusers (dropdown list)
        [HttpGet("{parentCompanyId:guid}/superusers")]
        public async Task<IActionResult> GetParentSuperUsers(Guid parentCompanyId)
        {
            var users = await _companyService.GetParentCompanySuperUsersAsync(parentCompanyId);

            // map to minimal DTO for dropdown (UserId, FullName, Email)
            var dto = users.Select(u => new
            {
                u.UserId,
                FullName = $"{u.FirstName} {(u.LastName ?? "")}".Trim(),
                u.Email
            });

            return Ok(dto);
        }

        // POST: /api/companies/{parentCompanyId}/subcompanies/{subCompanyId}/assign-superusers
        [HttpPost("{parentCompanyId:guid}/subcompanies/{subCompanyId:guid}/assign-superusers")]
        public async Task<IActionResult> AssignSuperUsers(
            Guid parentCompanyId,
            Guid subCompanyId,
            [FromBody] AssignSuperUsersDto dto)
        {
            try
            {
                // authorization: verify caller is allowed
                await _companyService.AssignSuperUsersToSubCompanyAsync(
                    parentCompanyId,
                    subCompanyId,
                    dto.UserIds,
                    replaceExisting: true);

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Add this to your CompanyController.cs

        [HttpGet("{parentCompanyId:guid}/user-assignments/{userId:int}")]
        [Authorize]
        public async Task<IActionResult> GetUserCompanyAssignments(Guid parentCompanyId, int userId)
        {
            try
            {
                // Verify the requesting user has permission
                var callerCompanyClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                var callerUserIdClaim = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;

                if (string.IsNullOrEmpty(callerCompanyClaim) || string.IsNullOrEmpty(callerUserIdClaim))
                    return Forbid();

                var callerCompanyId = Guid.Parse(callerCompanyClaim);
                var callerUserId = int.Parse(callerUserIdClaim);

                // Ensure the request is for the same company or authorized
                if (callerCompanyId != parentCompanyId)
                    return Forbid();

                // Get subcompany assignments
                var subCompanyAssignments = await _db.CompanySuperUserAssignments
                    .Where(a => a.UserId == userId)
                    .Include(a => a.Company)
                    .Select(a => new
                    {
                        CompanyId = a.Company.CompanyId,
                        CompanyName = a.Company.CompanyName,
                        Location = a.Company.Location,
                        CompanyType = "Sub Company",
                        IsParent = false
                    })
                    .ToListAsync();

                // Always include the parent company as an option for parent company superusers
                var parentCompany = await _db.Companies
                    .Where(c => c.CompanyId == parentCompanyId)
                    .Select(c => new
                    {
                        CompanyId = c.CompanyId,
                        CompanyName = c.CompanyName + " (Parent)",
                        Location = c.Location,
                        CompanyType = "Parent Company",
                        IsParent = true
                    })
                    .FirstOrDefaultAsync();

                var allCompanies = new List<object>();

                // Add parent company first
                if (parentCompany != null)
                {
                    allCompanies.Add(parentCompany);
                }

                // Add sub-companies
                allCompanies.AddRange(subCompanyAssignments);

                return Ok(allCompanies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user company assignments");
                return StatusCode(500, new { error = "An error occurred while fetching company assignments." });
            }
        }
    }
}