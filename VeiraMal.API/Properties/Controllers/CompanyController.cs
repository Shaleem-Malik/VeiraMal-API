using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Properties.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CompanyController : ControllerBase
    {
        private readonly ICompanyService _companyService;
        private readonly IStripeService _stripeService;
        private readonly ILogger<CompanyController> _logger;
        private readonly ISubCompanyResolver _subCompanyResolver;
        private readonly AppDbContext _db;
        private readonly IConfiguration _cfg;
        private readonly IWebHostEnvironment _env;
        private readonly IAbnLookupService _abnLookupService;

        public CompanyController(
            ICompanyService companyService,
            ISubCompanyResolver subCompanyResolver,
            AppDbContext db,
            ILogger<CompanyController> logger,
             IConfiguration cfg,
             IStripeService stripeService,
             IWebHostEnvironment env,
             IAbnLookupService abnLookupService)
        {
            _companyService = companyService;
            _stripeService = stripeService;
            _subCompanyResolver = subCompanyResolver;
            _db = db;
            _logger = logger;
            _cfg = cfg;
            _env = env;
            _abnLookupService = abnLookupService;
        }

        [HttpPost("onboard")]
        public async Task<IActionResult> Onboard([FromBody] CompanyOnboardRequestDto request)
        {
            if (request == null || request.Dto == null)
                return BadRequest(new { message = "Invalid request." });

            try
            {
                var abn = request.Dto.CompanyABN?.Trim();

                if (string.IsNullOrWhiteSpace(abn))
                {
                    return BadRequest(new
                    {
                        message = "Company ABN is required and must be validated before signup."
                    });
                }

                var abnResult = await _abnLookupService.ValidateAbnAsync(abn);

                if (!abnResult.IsValid)
                {
                    return BadRequest(new
                    {
                        message = "Signup blocked because ABN is not valid.",
                        abnError = abnResult.Message
                    });
                }

                // Optional: overwrite with cleaned ABN digits only
                request.Dto.CompanyABN = abnResult.Abn ?? abn;

                // Create company & user, but do not send email yet
                var res = await _companyService.OnboardCompanyAsync(
                    request.Dto,
                    request.SignInUrl,
                    sendEmail: false);

                // Create Stripe session for the computed amount
                var session = await _stripeService.CreateCheckoutSessionAsync(
                    res.CompanyId,
                    res.UserId,
                    res.CompanySubscriptionId,
                    res.AmountInCents,
                    request.SuccessUrl,
                    request.CancelUrl,
                    request.Currency ?? "aud"
                );

                return Ok(new
                {
                    sessionId = session.Id,
                    url = session.Url
                });
            }
            catch (ArgumentException aex)
            {
                return BadRequest(new { message = aex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onboard failed");
                return StatusCode(500, new { message = "Onboarding failed.", details = ex.Message });
            }
        }
        [HttpPost("resend-onboarding")]
        public async Task<IActionResult> ResendOnboarding([FromBody] ResendOnboardRequest req)
        {
            if (req == null) return BadRequest("Invalid request.");
            try
            {
                var signinUrl = _cfg.GetValue<string>("App:SigninUrl") ?? "http://localhost:3000/signin";
                await _companyService.FinalizeOnboardPaymentAsync(req.CompanyId, req.UserId, req.CompanySubscriptionId, signinUrl);
                return Ok(new { message = "Onboarding email sent (if protected temp password existed)." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResendOnboarding failed for CompanyId={CompanyId}", req?.CompanyId);
                return StatusCode(500, new { message = "Failed to resend onboarding email.", details = ex.Message });
            }
        }

        public class ResendOnboardRequest
        {
            public Guid CompanyId { get; set; }
            public int UserId { get; set; }
            public Guid CompanySubscriptionId { get; set; }
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

        // POST api/companies/effective/logo
        [HttpPost("effective/logo")]
        [Authorize]
        public async Task<IActionResult> UploadEffectiveCompanyLogo([FromQuery] Guid? subCompanyId)
        {
            try
            {
                if (!Request.HasFormContentType)
                    return BadRequest(new { message = "Expecting multipart/form-data with file field." });

                // get caller claims
                var baseCompanyClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                var callerUserClaim = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
                if (string.IsNullOrEmpty(baseCompanyClaim) || string.IsNullOrEmpty(callerUserClaim))
                    return Forbid();

                var baseCompanyId = Guid.Parse(baseCompanyClaim);
                var callerUserId = int.Parse(callerUserClaim);

                // resolve the target company (same pattern used elsewhere)
                var targetCompanyId = await _subCompanyResolver.ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);

                // permission check: ensure caller is superUser of base company
                var caller = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == callerUserId && u.CompanyId == baseCompanyId);

                if (caller == null || !string.Equals(caller.AccessLevel, "superUser", StringComparison.OrdinalIgnoreCase))
                    return Forbid();

                var file = Request.Form.Files.FirstOrDefault();
                if (file == null || file.Length == 0)
                    return BadRequest(new { message = "File is required." });

                const long MAX_BYTES = 2 * 1024 * 1024; // 2 MB
                if (file.Length > MAX_BYTES)
                    return BadRequest(new { message = "File too large. Max 2 MB allowed." });

                var permitted = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
                if (!permitted.Contains(file.ContentType?.ToLowerInvariant()))
                    return BadRequest(new { message = "Invalid file type. Allowed: jpg, png, webp." });

                // Prepare folder: wwwroot/uploads/logos/{companyId}
                var uploadsRoot = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "logos", targetCompanyId.ToString());
                if (!Directory.Exists(uploadsRoot)) Directory.CreateDirectory(uploadsRoot);

                // Determine extension
                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(ext))
                {
                    ext = file.ContentType switch
                    {
                        "image/png" => ".png",
                        "image/webp" => ".webp",
                        _ => ".jpg"
                    };
                }

                // Build filename and remove old files
                var newFileName = $"logo{ext}"; // simple stable file name
                var destPath = Path.Combine(uploadsRoot, newFileName);

                // remove existing files in folder (if any)
                var existing = Directory.EnumerateFiles(uploadsRoot, "logo.*", SearchOption.TopDirectoryOnly).ToList();
                foreach (var e in existing)
                {
                    try { System.IO.File.Delete(e); } catch { /* ignore */ }
                }

                // save file
                using (var stream = new FileStream(destPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // store public URL e.g. /uploads/logos/{companyId}/logo.png
                var publicUrl = $"/uploads/logos/{targetCompanyId}/{newFileName}";

                // update DB
                var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == targetCompanyId);
                if (company == null) return NotFound(new { message = "Company not found." });

                company.LogoUrl = publicUrl;
                _db.Companies.Update(company);
                await _db.SaveChangesAsync();

                return Ok(new { message = "Logo uploaded.", logoUrl = publicUrl });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UploadEffectiveCompanyLogo failed");
                return StatusCode(500, new { message = "Upload failed.", details = ex.Message });
            }
        }

        // DELETE api/companies/effective/logo
        [HttpDelete("effective/logo")]
        [Authorize]
        public async Task<IActionResult> DeleteEffectiveCompanyLogo([FromQuery] Guid? subCompanyId)
        {
            try
            {
                var baseCompanyClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
                var callerUserClaim = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
                if (string.IsNullOrEmpty(baseCompanyClaim) || string.IsNullOrEmpty(callerUserClaim))
                    return Forbid();

                var baseCompanyId = Guid.Parse(baseCompanyClaim);
                var callerUserId = int.Parse(callerUserClaim);

                var targetCompanyId = await _subCompanyResolver.ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);

                var caller = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == callerUserId && u.CompanyId == baseCompanyId);

                if (caller == null || !string.Equals(caller.AccessLevel, "superUser", StringComparison.OrdinalIgnoreCase))
                    return Forbid();

                var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == targetCompanyId);
                if (company == null) return NotFound(new { message = "Company not found." });

                if (string.IsNullOrWhiteSpace(company.LogoUrl))
                    return BadRequest(new { message = "No logo to delete." });

                // physical file removal (if exists)
                try
                {
                    var relative = company.LogoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    var full = Path.Combine(_env.WebRootPath ?? "wwwroot", relative);
                    if (System.IO.File.Exists(full)) System.IO.File.Delete(full);

                    // Also remove folder if empty
                    var folder = Path.GetDirectoryName(full);
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        try { Directory.Delete(folder); } catch { /* ignore */ }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove logo file physically for company {CompanyId}", targetCompanyId);
                }

                company.LogoUrl = null;
                _db.Companies.Update(company);
                await _db.SaveChangesAsync();

                return Ok(new { message = "Logo removed." });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteEffectiveCompanyLogo failed");
                return StatusCode(500, new { message = "Delete failed.", details = ex.Message });
            }
        }
    }
}