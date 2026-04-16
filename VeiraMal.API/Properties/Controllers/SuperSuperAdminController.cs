using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VeiraMal.API;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    /// <summary>
    /// Controller for Super-Admin functionality:
    /// - list all companies
    /// - list users for company
    /// - toggle user active / inactive
    /// - reset user password (generate temp, set hash, send email)
    /// - impersonation: returns short-lived URL (contains JWT) to open in new tab
    /// </summary>
    [ApiController]
    [Route("api")]
    //[Authorize(Policy = "SuperAdminOnly")] // ensure you have registered this policy or replace with Roles = "SuperAdmin"
    public class SuperSuperAdminController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IUserManagementService _userMgmt;
        private readonly IUserService _userService;
        private readonly IEmailService _email;
        private readonly IConfiguration _cfg;
        private readonly ILogger<SuperAdminController> _logger;

        public SuperSuperAdminController(
            AppDbContext db,
            IUserManagementService userMgmt,
            IUserService userService,
            IEmailService email,
            IConfiguration cfg,
            ILogger<SuperAdminController> logger)
        {
            _db = db;
            _userMgmt = userMgmt;
            _userService = userService;
            _email = email;
            _cfg = cfg;
            _logger = logger;
        }

        #region DTOs
        public class CompanyListDto
        {
            public Guid CompanyId { get; set; }
            public string CompanyName { get; set; } = default!;
            public string? ContactNumber { get; set; }
            public string? Location { get; set; }
            public Guid? ParentCompanyId { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class ImpersonationRequestDto
        {
            public Guid CompanyId { get; set; }
            public int? UserId { get; set; } // optional: impersonate a specific user
            public int ExpiresMinutes { get; set; } = 5; // short by default
            public string? RedirectPath { get; set; } // optional path to redirect to after impersonation
        }

        public class ImpersonationResultDto
        {
            public string ImpersonationUrl { get; set; } = default!;
            public DateTime ExpiresAt { get; set; }
        }
        #endregion

        /// <summary>
        /// GET /api/admin/companies
        /// Returns simple list of companies for SuperAdmin UI.
        /// </summary>
        [HttpGet("admin/companies")]
        public async Task<IActionResult> ListCompanies()
        {
            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.CompanyName)
                .Select(c => new CompanyListDto
                {
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyName,
                    ContactNumber = c.ContactNumber,
                    Location = c.Location,
                    ParentCompanyId = c.ParentCompanyId,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return Ok(companies);
        }

        /// <summary>
        /// GET /api/admin/companies/{companyId}/users
        /// Returns users for the specified company (uses existing service).
        /// </summary>
        [HttpGet("admin/companies/{companyId:guid}/users")]
        public async Task<IActionResult> ListCompanyUsers([FromRoute] Guid companyId)
        {
            // Reuse UserManagementService.ListUsersAsync which already handles parent/subcompany logic
            var users = await _userMgmt.ListUsersAsync(companyId);

            // Map to a minimal public DTO to avoid returning internal fields
            var result = users.Select(u => new
            {
                userId = u.UserId,
                userGuid = u.UserGuid,
                employeeNumber = u.EmployeeNumber,
                firstName = u.FirstName,
                lastName = u.LastName,
                email = u.Email,
                accessLevel = u.AccessLevel,
                isActive = u.IsActive,
                contactNumber = u.ContactNumber,
                location = u.Location,
                createdAt = u.CreatedAt
            }).OrderBy(u => u.employeeNumber).ToList();

            return Ok(result);
        }

        /// <summary>
        /// POST /api/admin/companies/{companyId}/users/{userId}/toggle-active
        /// Toggles the user's active flag.
        /// </summary>
        [HttpPost("admin/companies/{companyId:guid}/users/{userId:int}/toggle-active")]
        public async Task<IActionResult> ToggleUserActive([FromRoute] Guid companyId, [FromRoute] int userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.UserId == userId);
            if (user == null) return NotFound(new { message = "User not found for this company." });

            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();

            _logger.LogInformation("SuperAdmin {sa} toggled user {userId} for company {companyId} -> active={active}",
                User.Identity?.Name ?? "unknown", userId, companyId, user.IsActive);

            return Ok(new { userId = user.UserId, isActive = user.IsActive });
        }

        /// <summary>
        /// POST /api/admin/companies/{companyId}/users/{userId}/reset-password
        /// Generates temporary password, sets hash, marks password reset required, and emails the user.
        /// </summary>
        [HttpPost("admin/companies/{companyId:guid}/users/{userId:int}/reset-password")]
        public async Task<IActionResult> ResetUserPassword([FromRoute] Guid companyId, [FromRoute] int userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.UserId == userId);
            if (user == null) return NotFound(new { message = "User not found for this company." });

            try
            {
                var temp = await _userService.GenerateTemporaryPasswordAsync();
                await _userService.SetPasswordHashAsync(user, temp);

                user.IsPasswordResetRequired = true;
                user.IsFirstLogin = true;
                await _db.SaveChangesAsync();

                // Send email (basic template). You can replace with a nicer template or call an existing service method.
                var subject = "Password reset — temporary password";
                var html = $@"
                    <div style='font-family: Arial, sans-serif; max-width:600px; margin:0 auto;'>
                        <p>Hi {System.Net.WebUtility.HtmlEncode(user.FirstName)}</p>
                        <p>A password reset was requested for your account. Use the temporary password below to sign in and choose a new password.</p>
                        <div style='padding:10px;background:#f8f8f8;border-radius:6px;display:inline-block;font-family:monospace;'>
                            {System.Net.WebUtility.HtmlEncode(temp)}
                        </div>
                        <p>If you did not request this, contact your administrator.</p>
                    </div>";

                await _email.SendEmailAsync(user.Email, subject, html);

                _logger.LogInformation("SuperAdmin {sa} reset password for user {userId} company {companyId}",
                    User.Identity?.Name ?? "unknown", userId, companyId);

                return Ok(new { userId = user.UserId, message = "Password reset and email sent." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for user {userId} company {companyId}", userId, companyId);
                return StatusCode(500, new { message = "Failed to reset password." });
            }
        }

        /// <summary>
        /// POST /api/superadmin/impersonate
        /// Request body: { companyId, userId? }
        /// Returns a short-lived impersonationUrl which the frontend should open in NEW TAB.
        /// Implementation: create short-lived JWT with impersonation claims and return URL that points back to the app which will validate token.
        /// Security: token expiry should be small (5 minutes), and the impersonation endpoint on the client must validate token server-side and set tenant cookie/session.
        /// </summary>
        [HttpPost("superadmin/impersonate")]
        public async Task<IActionResult> Impersonate([FromBody] ImpersonationRequestDto req)
        {
            if (req == null || req.CompanyId == Guid.Empty) return BadRequest(new { message = "companyId is required." });

            // Basic check company exists
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == req.CompanyId);
            if (company == null) return NotFound(new { message = "Company not found." });

            // If userId supplied, validate the user belongs to that company (or parent/sub rules as required)
            if (req.UserId.HasValue)
            {
                var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == req.UserId.Value);
                if (u == null) return NotFound(new { message = "User not found." });

                // allow impersonation of parent users assigned to subcompany? For safety, check that the user is either in the target company or is assigned to that subcompany (mimic ListUsersAsync logic).
                var allowed = (u.CompanyId == req.CompanyId) || (u.CompanyId == company.ParentCompanyId && await _db.CompanySuperUserAssignments.AnyAsync(a => a.CompanyId == req.CompanyId && a.UserId == u.UserId));
                if (!allowed) return BadRequest(new { message = "User cannot be impersonated for the given company." });
            }

            try
            {
                var expires = DateTime.UtcNow.AddMinutes(Math.Clamp(req.ExpiresMinutes, 1, 60));
                var token = GenerateImpersonationJwt(req.CompanyId, req.UserId, expires);

                // Build redirect base: prefer configured ImpersonationRedirectBase in config (e.g., client app URL /auth/impersonate)
                var redirectBase = _cfg.GetValue<string>("Impersonation:RedirectBase");
                if (string.IsNullOrWhiteSpace(redirectBase))
                {
                    // fallback to client app url from configuration "ClientApp:BaseUrl" or env var; change as necessary
                    redirectBase = _cfg.GetValue<string>("ClientApp:BaseUrl") ?? "/";
                    if (!redirectBase.EndsWith("/")) redirectBase += "/";
                    redirectBase += "auth/impersonate";
                }

                // Optionally append a redirect path for deeper landing
                var redirectPath = string.IsNullOrWhiteSpace(req.RedirectPath) ? "" : $"&redirect={Uri.EscapeDataString(req.RedirectPath)}";

                var url = $"{redirectBase}?token={Uri.EscapeDataString(token)}{redirectPath}";
                return Ok(new ImpersonationResultDto { ImpersonationUrl = url, ExpiresAt = expires });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create impersonation token for company {companyId}", req.CompanyId);
                return StatusCode(500, new { message = "Failed to create impersonation token." });
            }
        }

        /// <summary>
        /// Helper: generate a short-lived JWT with impersonation claims.
        /// The application that receives this token (client app server-side endpoint /auth/impersonate)
        /// should validate the token signature and then create an authenticated session for the impersonated tenant.
        /// </summary>
        private string GenerateImpersonationJwt(Guid companyId, int? userId, DateTime expiresUtc)
        {
            // Config keys used: Jwt:Key, Jwt:Issuer, Jwt:Audience
            var key = _cfg.GetValue<string>("Jwt:Key");
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Jwt:Key configuration is required for impersonation.");

            var issuer = _cfg.GetValue<string>("Jwt:Issuer") ?? "VeiraMal";
            var audience = _cfg.GetValue<string>("Jwt:Audience") ?? "VeiraMalClient";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var handler = new JwtSecurityTokenHandler();
            var claims = new[]
            {
                new Claim("impersonation", "true"),
                new Claim("companyId", companyId.ToString()),
                new Claim("superAdmin", User.Identity?.Name ?? "superadmin"),
                // include userId only if present
            }.ToList();

            if (userId.HasValue) claims.Add(new Claim("userId", userId.Value.ToString()));

            var token = handler.CreateJwtSecurityToken(
                issuer: issuer,
                audience: audience,
                subject: new ClaimsIdentity(claims),
                notBefore: now,
                expires: expiresUtc,
                signingCredentials: creds
            );

            return handler.WriteToken(token);
        }
    }
}