using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using VeiraMal.API;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using VeiraMal.API.Services;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;
        private readonly IEmailService _emailService;
        private readonly PasswordValidator _passwordValidator;

        public AuthController(IUserService userService, AppDbContext db, IConfiguration cfg, IEmailService emailService, PasswordValidator passwordValidator)
        {
            _userService = userService;
            _db = db;
            _cfg = cfg;
            _emailService = emailService;
            _passwordValidator = passwordValidator;
        }

        #region Existing endpoints (unchanged)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _userService.GetByEmailAsync(dto.Email);
            if (user == null)
                return Unauthorized(new { Message = "Invalid credentials" });

            if (!user.IsActive)
                return Unauthorized(new { Message = "Account is inactive. Please contact your administrator." });

            var ok = await _userService.VerifyPasswordAsync(user, dto.Password);
            if (!ok)
                return Unauthorized(new { Message = "Invalid credentials" });

            var businessUnits = ParseBusinessUnits(user.BusinessUnit);

            // Capture original flag BEFORE changing it
            var wasFirstLogin = user.IsFirstLogin;

            if (wasFirstLogin)
            {
                user.IsFirstLogin = false;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();
            }

            // pass original wasFirstLogin into token generation (see below)
            var token = GenerateJwtToken(user, includeMustReset: user.IsPasswordResetRequired, businessUnits: businessUnits, isFirstLogin: wasFirstLogin);

            var resp = new AuthResponseDto
            {
                Token = token,
                MustResetPassword = user.IsPasswordResetRequired,
                IsFirstLogin = wasFirstLogin, // now correctly reports whether this *was* first login
                Message = user.IsPasswordResetRequired ? "Password reset required" : "Login successful",
                BusinessUnits = businessUnits
            };

            return Ok(resp);
        }

        [Authorize]
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            // find user from claims
            var userIdClaim = User.FindFirst("userId")?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var user = await _db.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // set new password
            await _userService.SetPasswordHashAsync(user, dto.NewPassword);
            user.IsPasswordResetRequired = false;

            // This makes the next successful login behave like a first-login (client receives IsFirstLogin true).
            user.IsFirstLogin = true;

            await _db.SaveChangesAsync();

            // send welcome email
            var subject = $"Welcome to {(await _db.Companies.FindAsync(user.CompanyId))?.CompanyName ?? "our app"}";
            var signinUrl = "https://dev.hranalytix.com/VeiraMal-Project/signin";
            var body = $@"
                <!doctype html>
                <html>
                  <body style=""margin:0;padding:0;background-color:#f4f6f8;font-family: Arial, sans-serif;"">
                    <table width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background-color:#f4f6f8;padding:20px 0;"">
                      <tr>
                        <td align=""center"">
                          <table width=""600"" cellspacing=""0"" cellpadding=""0"" style=""background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 4px 18px rgba(0,0,0,0.06);"">
                            <tr>
                              <td style=""padding:32px;"">
                                <p style=""margin:0 0 16px 0;color:#333;font-size:16px;"">Hi {user.FirstName},</p>
                                <p style=""margin:0 0 20px 0;color:#666;font-size:14px;line-height:1.5;"">
                                  Your password has been updated successfully. You can sign in here: 
                                  <a href='{signinUrl}' style=""color:#0f6efd;text-decoration:none;font-weight:600;"">Sign in</a>
                                </p>
                                <p style=""margin:0;color:#666;font-size:14px;"">Welcome aboard!</p>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>
                  </body>
                </html>";
            await _emailService.SendEmailAsync(user.Email, subject, body);

            return Ok(new { Message = "Password updated and welcome email sent." });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // Read token from Authorization header
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { Message = "Authorization header missing or invalid." });

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var handler = new JwtSecurityTokenHandler();

            JwtSecurityToken? jwt;
            try
            {
                jwt = handler.ReadJwtToken(token);
            }
            catch
            {
                return BadRequest(new { Message = "Invalid token." });
            }

            var jti = jwt?.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrWhiteSpace(jti))
                return BadRequest(new { Message = "Token missing identifier (jti)." });

            // expiration
            var expClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
            DateTime expiresAtUtc;
            if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expSeconds))
            {
                expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
            }
            else
            {
                // fallback: use token.ValidTo
                expiresAtUtc = jwt.ValidTo.ToUniversalTime();
            }

            // userId optional
            var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
            int? userId = null;
            if (int.TryParse(userIdClaim, out var uid)) userId = uid;

            var blacklist = HttpContext.RequestServices.GetRequiredService<ITokenBlacklistService>();
            await blacklist.RevokeTokenAsync(jti, expiresAtUtc, userId, reason: "User logout");

            return Ok(new { Message = "Logged out successfully." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { Message = "Email is required." });

            // find user by email (case-insensitive lookup should be inside GetByEmailAsync, but ensure)
            var user = await _userService.GetByEmailAsync(dto.Email.Trim());
            if (user == null)
            {
                // Option A: return NotFound so UI can say 'email not registered'
                // return NotFound(new { Message = "Email not registered." });

                // Option B (more secure): don't reveal whether the email is registered:
                // return Ok(new { Message = "If an account exists for that email, a reset link has been sent." });
                return NotFound(new { Message = "Email not registered." });
            }

            if (!user.IsActive)
            {
                return BadRequest(new { Message = "Account is inactive. Please contact your administrator." });
            }

            // generate and set temporary password
            var tempPassword = await _userService.GenerateTemporaryPasswordAsync();
            await _userService.SetPasswordHashAsync(user, tempPassword);

            // mark that they must reset password on next login
            user.IsPasswordResetRequired = true;

            // NOTE: we intentionally do NOT change IsFirstLogin here — forgot-password is separate from initial onboarding.

            await _db.SaveChangesAsync();

            // send temporary password email
            var subject = "Password reset - temporary password";
            var signinUrl = $"{Request.Scheme}://{Request.Host.Value}/signin";
            var body = $@"
                <div style='font-family: Arial, sans-serif; max-width:600px;margin:0 auto;'>
                  <div style='padding:24px;background:#ffffff;border-radius:8px;'>
                    <p>Hi {user.FirstName},</p>
                    <p>We received a request to reset your password. Use the temporary password below to sign in, then you will be prompted to set a new password.</p>
                    <div style='background:#f4f6f8;padding:12px;border-radius:6px;margin:16px 0;'>
                      <p style='margin:0;'><strong>Temporary password:</strong></p>
                      <p style='margin:8px 0 0 0;font-family:monospace;word-break:break-all;'>{tempPassword}</p>
                    </div>
                    <p style='margin-top:12px;'>Sign in here: <a href='{signinUrl}' style='color:#0f6efd'>{signinUrl}</a></p>
                    <p style='font-size:12px;color:#666;margin-top:12px;'>If you did not request this password reset, please contact your administrator immediately.</p>
                    <p style='margin-top:16px;'>Regards,<br/>HR Analytix Team</p>
                  </div>
                </div>";

            await _emailService.SendEmailAsync(user.Email, subject, body);

            return Ok(new { Message = "Temporary password has been sent to the registered email address." });
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { Message = "Current password and new password are required." });

            // Trim whitespace from passwords
            dto.CurrentPassword = dto.CurrentPassword.Trim();
            dto.NewPassword = dto.NewPassword.Trim();

            // Check if new password is not empty after trim
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { Message = "New password cannot be empty or whitespace." });

            // Use the injected password validator
            var (isValid, message) = _passwordValidator.ValidatePassword(dto.NewPassword);
            if (!isValid)
                return BadRequest(new { Message = message });

            // Get user from claims
            var userIdClaim = User.FindFirst("userId")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { Message = "Invalid user token." });

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return NotFound(new { Message = "User not found." });

            // Verify current password
            var isCurrentPasswordValid = await _userService.VerifyPasswordAsync(user, dto.CurrentPassword);
            if (!isCurrentPasswordValid)
                return BadRequest(new { Message = "Current password is incorrect." });

            // Check if new password is same as current (optional but recommended)
            // Note: Use string.Equals for comparison if case sensitivity matters
            if (dto.CurrentPassword.Equals(dto.NewPassword, StringComparison.Ordinal))
                return BadRequest(new { Message = "New password must be different from current password." });

            // Set new password
            await _userService.SetPasswordHashAsync(user, dto.NewPassword);

            // IMPORTANT: DO NOT set IsPasswordResetRequired = true
            // The user should NOT be forced to reset password on next login
            user.IsPasswordResetRequired = false;
            // Keep IsFirstLogin as is (false if they've logged in before)

            await _db.SaveChangesAsync();

            return Ok(new { Message = "Password changed successfully. You can now log in with your new password." });
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            // get userId from token claims
            var userIdClaim = User.FindFirst("userId")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { Message = "Invalid user token." });

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized(new { Message = "User not found." });

            // If you want to guard refresh (e.g., prevent refresh if account inactive)
            if (!user.IsActive)
                return Unauthorized(new { Message = "Account inactive." });

            // Build business units for token (re-using existing helper)
            var businessUnits = ParseBusinessUnits(user.BusinessUnit);

            // Generate a fresh token -- this uses the existing GenerateJwtToken method you already have
            var newToken = GenerateJwtToken(user, includeMustReset: user.IsPasswordResetRequired, businessUnits: businessUnits, isFirstLogin: false);

            return Ok(new { Token = newToken });
        }
        #endregion

        #region Impersonation accept endpoint (NEW)
        // DTO used for incoming accept request
        public class AcceptImpersonationDto
        {
            public string Token { get; set; } = "";
            public string? Redirect { get; set; } // optional redirect path (e.g., "/app/dashboard")
        }

        /// <summary>
        /// POST /api/auth/impersonate/accept
        /// Accepts the short-lived impersonation token (created by SuperAdmin) and
        /// creates a cookie-based session for the impersonated tenant in this new tab.
        /// Returns { redirectUrl } which client should navigate to.
        /// This endpoint must be AllowAnonymous because the token itself is the proof.
        /// </summary>
        [HttpPost("impersonate/accept")]
        [AllowAnonymous]
        public async Task<IActionResult> AcceptImpersonation([FromBody] AcceptImpersonationDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(new { message = "Token is required." });

            try
            {
                // Read JWT validation parameters from config
                var key = _cfg.GetValue<string>("Jwt:Key");
                var issuer = _cfg.GetValue<string>("Jwt:Issuer");
                var audience = _cfg.GetValue<string>("Jwt:Audience");

                if (string.IsNullOrWhiteSpace(key))
                    return StatusCode(500, new { message = "Server not configured for impersonation (missing Jwt:Key)." });

                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
                    ValidIssuer = issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Validate token and obtain principal
                ClaimsPrincipal principal;
                try
                {
                    principal = tokenHandler.ValidateToken(dto.Token, validationParameters, out var validatedToken);
                }
                catch (SecurityTokenExpiredException)
                {
                    return BadRequest(new { message = "Impersonation token has expired." });
                }
                catch (Exception ex)
                {
                    return BadRequest(new { message = $"Invalid impersonation token: {ex.Message}" });
                }

                // Verify impersonation claim
                var impClaim = principal.Claims.FirstOrDefault(c => c.Type == "impersonation" && c.Value == "true");
                if (impClaim == null)
                    return BadRequest(new { message = "Token is not an impersonation token." });

                // Extract companyId
                var companyIdClaim = principal.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                if (string.IsNullOrWhiteSpace(companyIdClaim) || !Guid.TryParse(companyIdClaim, out var companyId))
                    return BadRequest(new { message = "companyId claim missing or invalid." });

                var company = await _db.Companies.FindAsync(companyId);
                if (company == null)
                    return BadRequest(new { message = "Company not found." });

                // Optional: if userId provided, validate allowed
                int? impersonateUserId = null;
                var userIdClaim = principal.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
                if (!string.IsNullOrWhiteSpace(userIdClaim) && int.TryParse(userIdClaim, out var parsedUid))
                {
                    var user = await _db.Users.FindAsync(parsedUid);
                    if (user == null)
                        return BadRequest(new { message = "Impersonated user not found." });

                    // allow impersonation if user belongs to the target company OR is a parent-company user assigned to this subcompany
                    var allowed = user.CompanyId == companyId;
                    if (!allowed && company.ParentCompanyId.HasValue)
                    {
                        var parentId = company.ParentCompanyId.Value;
                        if (user.CompanyId == parentId)
                        {
                            var assigned = await _db.CompanySuperUserAssignments.AnyAsync(a => a.CompanyId == companyId && a.UserId == user.UserId);
                            allowed = assigned;
                        }
                    }

                    if (!allowed)
                        return BadRequest(new { message = "User cannot be impersonated for the given company." });

                    impersonateUserId = parsedUid;
                }

                // OPTIONAL: Add replay-protection here
                // e.g., check a DB table or cache to ensure the token hash hasn't been used already.
                // If you want one-time use, store a short hash of `dto.Token` at token creation time and verify here.

                // Build claims for the new cookie principal that the application will use.
                var cookieClaims = new List<Claim>
                {
                    new Claim("companyId", companyId.ToString()),
                    new Claim("isImpersonation", "true")
                };

                // include impersonated user details if present
                if (impersonateUserId.HasValue)
                    cookieClaims.Add(new Claim("userId", impersonateUserId.Value.ToString()));

                // include who performed the impersonation (from the original token)
                var performedBy = principal.Claims.FirstOrDefault(c => c.Type == "superAdmin")?.Value ?? principal.Identity?.Name ?? "superadmin";
                cookieClaims.Add(new Claim("impersonatedBy", performedBy));

                // add a name identifier (unique per session)
                cookieClaims.Add(new Claim(ClaimTypes.NameIdentifier, $"impersonation-{Guid.NewGuid()}"));

                var identity = new ClaimsIdentity(cookieClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                var newPrincipal = new ClaimsPrincipal(identity);

                // Sign-in using cookie auth so the new tab receives the cookie
                var props = new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20) // short lived
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, newPrincipal, props);

                // Build redirect URL to client app
                var clientBase = _cfg.GetValue<string>("ClientApp:BaseUrl") ?? "http://localhost:3000";
                var redirectPath = string.IsNullOrWhiteSpace(dto.Redirect) ? "/app/dashboard" : dto.Redirect;
                var redirectUrl = clientBase.TrimEnd('/') + (redirectPath.StartsWith('/') ? redirectPath : "/" + redirectPath);

                // Return redirect url (frontend will navigate there)
                return Ok(new { redirectUrl });
            }
            catch (Exception ex)
            {
                // do not leak sensitive details
                return StatusCode(500, new { message = "Failed to accept impersonation." });
            }
        }
        #endregion

        #region Helpers (existing)
        private string GenerateJwtToken(User user, bool includeMustReset, List<string>? businessUnits = null, bool isFirstLogin = false)
        {
            var key = _cfg["Jwt:Key"];
            var issuer = _cfg["Jwt:Issuer"];
            var audience = _cfg["Jwt:Audience"];
            var expiryMinutes = int.Parse(_cfg["Jwt:ExpiryMinutes"] ?? "60");

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var jti = Guid.NewGuid().ToString();

            // base claims
            var claims = new List<Claim>
            {
                new Claim("userId", user.UserId.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("companyId", user.CompanyId.ToString()),
                new Claim("firstName", user.FirstName),
                new Claim("access", user.AccessLevel),
                new Claim("isFirstLogin", isFirstLogin ? "true" : "false"), // use captured value
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            };

            // include business units (both a single comma-separated claim and multiple claims)
            if (businessUnits != null && businessUnits.Count > 0)
            {
                var normalized = businessUnits
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (normalized.Count > 0)
                {
                    claims.Add(new Claim("businessUnits", string.Join(",", normalized)));
                    foreach (var bu in normalized)
                    {
                        claims.Add(new Claim("businessUnit", bu));
                    }
                }
            }

            var jwt = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            if (includeMustReset)
            {
                jwt.Payload["must_reset"] = true;
            }

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        private static List<string> ParseBusinessUnits(string? dbValue)
        {
            if (string.IsNullOrWhiteSpace(dbValue)) return new List<string>();

            return dbValue
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        #endregion
    }
}