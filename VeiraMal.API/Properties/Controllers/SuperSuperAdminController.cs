using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using VeiraMal.API;
using VeiraMal.API.Models;
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
    public class SuperSuperAdminController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IUserManagementService _userMgmt;
        private readonly IUserService _userService;
        private readonly IEmailService _email;
        private readonly IConfiguration _cfg;
        private readonly ILogger<SuperSuperAdminController> _logger;

        public SuperSuperAdminController(
            AppDbContext db,
            IUserManagementService userMgmt,
            IUserService userService,
            IEmailService email,
            IConfiguration cfg,
            ILogger<SuperSuperAdminController> logger)
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
            public int? UserId { get; set; }
            public int ExpiresMinutes { get; set; } = 5;
            public string? RedirectPath { get; set; }
        }

        public class ImpersonationResultDto
        {
            public string ImpersonationUrl { get; set; } = default!;
            public string Token { get; set; } = default!;
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
        /// Returns users for the specified company.
        /// </summary>
        [HttpGet("admin/companies/{companyId:guid}/users")]
        public async Task<IActionResult> ListCompanyUsers([FromRoute] Guid companyId)
        {
            var users = await _userMgmt.ListUsersAsync(companyId);

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
                businessUnit = u.BusinessUnit,
                createdAt = u.CreatedAt
            })
            .OrderBy(u => u.employeeNumber)
            .ToList();

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
            if (user == null)
                return NotFound(new { message = "User not found for this company." });

            user.IsActive = !user.IsActive;
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "SuperAdmin {sa} toggled user {userId} for company {companyId} -> active={active}",
                User.Identity?.Name ?? "unknown",
                userId,
                companyId,
                user.IsActive);

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
            if (user == null)
                return NotFound(new { message = "User not found for this company." });

            try
            {
                var temp = await _userService.GenerateTemporaryPasswordAsync();
                await _userService.SetPasswordHashAsync(user, temp);

                user.IsPasswordResetRequired = true;
                user.IsFirstLogin = true;
                await _db.SaveChangesAsync();

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

                _logger.LogInformation(
                    "SuperAdmin {sa} reset password for user {userId} company {companyId}",
                    User.Identity?.Name ?? "unknown",
                    userId,
                    companyId);

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
        /// Returns impersonationUrl + raw token.
        /// </summary>
        [HttpPost("superadmin/impersonate")]
        public async Task<IActionResult> Impersonate([FromBody] ImpersonationRequestDto req)
        {
            if (req == null || req.CompanyId == Guid.Empty)
                return BadRequest(new { message = "companyId is required." });

            var company = await _db.Companies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == req.CompanyId);

            if (company == null)
                return NotFound(new { message = "Company not found." });

            User? targetUser = null;

            if (req.UserId.HasValue)
            {
                targetUser = await _db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == req.UserId.Value);

                if (targetUser == null)
                    return NotFound(new { message = "User not found." });

                var allowed =
                    targetUser.CompanyId == req.CompanyId ||
                    (company.ParentCompanyId.HasValue &&
                     targetUser.CompanyId == company.ParentCompanyId.Value &&
                     await _db.CompanySuperUserAssignments.AnyAsync(a =>
                         a.CompanyId == req.CompanyId && a.UserId == targetUser.UserId));

                if (!allowed)
                    return BadRequest(new { message = "User cannot be impersonated for the given company." });
            }
            else
            {
                var users = await _db.Users.AsNoTracking()
                    .Where(u => u.CompanyId == req.CompanyId && u.IsActive)
                    .ToListAsync();

                targetUser = users
                    .OrderBy(u => GetImpersonationPriority(u.AccessLevel))
                    .ThenBy(u => u.EmployeeNumber)
                    .FirstOrDefault();

                if (targetUser == null)
                    return BadRequest(new { message = "No active users found for this company." });
            }

            try
            {
                var expires = DateTime.UtcNow.AddMinutes(Math.Clamp(req.ExpiresMinutes, 1, 60));
                var token = GenerateImpersonationJwt(req.CompanyId, targetUser, expires);

                var redirectBase = _cfg.GetValue<string>("Impersonation:RedirectBase");
                if (string.IsNullOrWhiteSpace(redirectBase))
                {
                    redirectBase = _cfg.GetValue<string>("ClientApp:BaseUrl") ?? "/";
                    if (!redirectBase.EndsWith("/")) redirectBase += "/";
                    redirectBase += "auth/impersonate";
                }

                var redirectPath = string.IsNullOrWhiteSpace(req.RedirectPath)
                    ? ""
                    : $"&redirect={Uri.EscapeDataString(req.RedirectPath)}";

                var url = $"{redirectBase}?token={Uri.EscapeDataString(token)}{redirectPath}";

                return Ok(new ImpersonationResultDto
                {
                    ImpersonationUrl = url,
                    Token = token,
                    ExpiresAt = expires
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create impersonation token for company {companyId}", req.CompanyId);
                return StatusCode(500, new { message = "Failed to create impersonation token." });
            }
        }

        private static int GetImpersonationPriority(string? accessLevel)
        {
            return accessLevel?.Trim().ToLowerInvariant() switch
            {
                "ceo" => 0,
                "superadmin" => 1,
                "teammanager" => 2,
                _ => 3
            };
        }

        /// <summary>
        /// Creates a short-lived JWT containing the target user's claims.
        /// </summary>
        private string GenerateImpersonationJwt(Guid companyId, User targetUser, DateTime expiresUtc)
        {
            var key = _cfg.GetValue<string>("Jwt:Key");
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Jwt:Key configuration is required for impersonation.");

            var issuer = _cfg.GetValue<string>("Jwt:Issuer") ?? "VeiraMal";
            var audience = _cfg.GetValue<string>("Jwt:Audience") ?? "VeiraMalClient";

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim("impersonation", "true"),
                new Claim("userId", targetUser.UserId.ToString()),
                new Claim("companyId", companyId.ToString()),
                new Claim("access", targetUser.AccessLevel ?? ""),
                new Claim("businessUnit", targetUser.BusinessUnit ?? "")
            };

            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateJwtSecurityToken(
                issuer: issuer,
                audience: audience,
                subject: new ClaimsIdentity(claims),
                notBefore: DateTime.UtcNow,
                expires: expiresUtc,
                signingCredentials: creds
            );

            return handler.WriteToken(token);
        }
    }
}