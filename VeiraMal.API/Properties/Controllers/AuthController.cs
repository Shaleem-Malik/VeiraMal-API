using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using VeiraMal.API;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
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

        public AuthController(IUserService userService, AppDbContext db, IConfiguration cfg, IEmailService emailService)
        {
            _userService = userService;
            _db = db;
            _cfg = cfg;
            _emailService = emailService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _userService.GetByEmailAsync(dto.Email);
            if (user == null)
                return Unauthorized(new { Message = "Invalid credentials" });

            // Check if user is inactive
            if (user.IsActive == false)
                return Unauthorized(new { Message = "Account is inactive. Please contact your administrator." });

            var ok = await _userService.VerifyPasswordAsync(user, dto.Password);
            if (!ok)
                return Unauthorized(new { Message = "Invalid credentials" });

            // parse business units from DB (comma separated), normalize and dedupe
            var businessUnits = ParseBusinessUnits(user.BusinessUnit);

            // BEFORE generating token: set IsFirstLogin = false (user has successfully logged in)
            // We persist this change to DB immediately.
            if (user.IsFirstLogin)
            {
                user.IsFirstLogin = false;
                _db.Users.Update(user);
                await _db.SaveChangesAsync();
            }

            var token = GenerateJwtToken(user, includeMustReset: user.IsPasswordResetRequired, businessUnits: businessUnits);

            var resp = new AuthResponseDto
            {
                Token = token,
                MustResetPassword = user.IsPasswordResetRequired,
                IsFirstLogin = user.IsFirstLogin, // now false after we set it
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
            await _db.SaveChangesAsync();

            // send welcome email
            var subject = $"Welcome to {(await _db.Companies.FindAsync(user.CompanyId))?.CompanyName ?? "our app"}";
            var signinUrl = $"{Request.Scheme}://{Request.Host.Value}/signin";
            var body = $@"<p>Hi {user.FirstName},</p>
                         <p>Your password has been updated successfully. You can sign in here: <a href='{signinUrl}'>Sign in</a></p>
                         <p>Welcome aboard!</p>";
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


        // Add `using System.Collections.Generic;` and `using System.Linq;` at top of file if not present.

        private string GenerateJwtToken(User user, bool includeMustReset, List<string>? businessUnits = null)
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
            new Claim("access", user.AccessLevel),
            new Claim("isFirstLogin", user.IsFirstLogin ? "true" : "false"), // <-- include flag
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
    }
}
