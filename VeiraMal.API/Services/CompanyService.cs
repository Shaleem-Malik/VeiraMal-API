using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly AppDbContext _db;
        private readonly IUserService _userService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _cfg;
        private readonly ILogger<CompanyService> _logger;
        private readonly IDataProtector _protector;

        // ABN must be exactly 11 digits
        private static readonly Regex AbnRegex =
            new Regex(@"^\d{11}$", RegexOptions.Compiled);

        private static readonly Guid EnterprisePlusPlanId =
            Guid.Parse("55555555-5555-5555-5555-555555555555");

        public CompanyService(
            AppDbContext db,
            IUserService userService,
            IEmailService emailService,
            IConfiguration cfg,
            ILogger<CompanyService> logger,
            IDataProtectionProvider dp)
        {
            _db = db;
            _userService = userService;
            _emailService = emailService;
            _cfg = cfg;
            _logger = logger;
            _protector = dp.CreateProtector(
                "VeiraMal.TempPasswordProtector.v1"
            );
        }

        // ============================================================
        // ONBOARD COMPANY
        // ============================================================

        public async Task<OnboardResultDto> OnboardCompanyAsync(
            CompanyOnboardDto dto,
            string signinLinkBase,
            bool sendEmail = true)
        {
            if (string.IsNullOrWhiteSpace(dto.SuperUserEmail))
                throw new ArgumentException("SuperUserEmail is required.");

            if (string.IsNullOrWhiteSpace(dto.SuperUserFirstName))
                throw new ArgumentException("SuperUserFirstName is required.");

            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                throw new ArgumentException("CompanyName is required.");

            var superUserEmailNormalized =
                dto.SuperUserEmail.Trim().ToUpperInvariant();

            var emailExists = await _db.Users
                .AsNoTracking()
                .AnyAsync(u =>
                    u.Email != null &&
                    u.Email.ToUpper() == superUserEmailNormalized);

            if (emailExists)
            {
                throw new ArgumentException(
                    "This email already exists. Please use a different email"
                );
            }

            if (!string.IsNullOrWhiteSpace(dto.CompanyABN))
            {
                var abn = dto.CompanyABN.Trim();

                if (!AbnRegex.IsMatch(abn))
                {
                    throw new ArgumentException(
                        "Company ABN must be exactly 11 digits."
                    );
                }
            }

            var plan = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(
                    p => p.SubscriptionPlanId == dto.SubscriptionPlanId
                );

            if (plan == null)
            {
                throw new ArgumentException(
                    "Subscription plan not found. Please choose a valid plan."
                );
            }

            if (dto.AdditionalSeatsRequested < 0)
            {
                throw new ArgumentException(
                    "AdditionalSeatsRequested cannot be negative."
                );
            }

            if (!plan.AdditionalSeatsAllowed &&
                dto.AdditionalSeatsRequested > 0)
            {
                throw new ArgumentException(
                    "This plan does not allow purchasing additional seats at signup. Contact sales."
                );
            }

            if (plan.MaxHC.HasValue)
            {
                var totalRequested =
                    (plan.BaseUserSeats == 0
                        ? int.MaxValue
                        : plan.BaseUserSeats)
                    + dto.AdditionalSeatsRequested;

                if (plan.BaseUserSeats != 0 &&
                    totalRequested > plan.MaxHC.Value)
                {
                    throw new ArgumentException(
                        $"Requested seats exceed plan's HC cap of {plan.MaxHC.Value}."
                    );
                }
            }

            if (!plan.PricePerMonth.HasValue ||
                plan.PricePerMonth.Value == 0m)
            {
                throw new ArgumentException(
                    "Selected plan requires a custom contract. Please contact sales to complete onboarding."
                );
            }

            var company = new Company
            {
                CompanyId = Guid.NewGuid(),
                CompanyName = dto.CompanyName.Trim(),
                CompanyABN = string.IsNullOrWhiteSpace(dto.CompanyABN)
                    ? null
                    : dto.CompanyABN.Trim(),
                ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber)
                    ? null
                    : dto.ContactNumber.Trim(),
                Location = string.IsNullOrWhiteSpace(dto.CompanyLocation)
                    ? null
                    : dto.CompanyLocation.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _db.Companies.AddAsync(company);
            await _db.SaveChangesAsync();

            var additionalPrice =
                plan.AdditionalSeatPrice ?? 0m;

            var additionalCost =
                additionalPrice * dto.AdditionalSeatsRequested;

            var monthlyPrice =
                (plan.PricePerMonth ?? 0m) + additionalCost;

            var companySubscription = new CompanySubscription
            {
                CompanyId = company.CompanyId,
                SubscriptionPlanId = plan.SubscriptionPlanId,
                PlanNameSnapshot = plan.Package,
                BaseUserSeatsSnapshot = plan.BaseUserSeats,
                AdditionalSeatsPurchased = dto.AdditionalSeatsRequested,
                AdditionalSeatPriceSnapshot = additionalPrice,
                MonthlyPriceSnapshot = monthlyPrice,
                StartDate = DateTime.UtcNow,
                IsPaid = false
            };

            var user = new User
            {
                CompanyId = company.CompanyId,
                EmployeeNumber = 1,
                FirstName = dto.SuperUserFirstName.Trim(),
                MiddleName =
                    string.IsNullOrWhiteSpace(dto.SuperUserMiddleName)
                        ? null
                        : dto.SuperUserMiddleName.Trim(),
                LastName =
                    string.IsNullOrWhiteSpace(dto.SuperUserLastName)
                        ? null
                        : dto.SuperUserLastName.Trim(),
                Email = dto.SuperUserEmail.Trim(),
                BusinessUnit = "Management",
                AccessLevel = "superUser",
                IsPasswordResetRequired = true,
                IsFirstLogin = true,
                ContactNumber =
                    string.IsNullOrWhiteSpace(dto.SuperUserContactNumber)
                        ? company.ContactNumber
                        : dto.SuperUserContactNumber.Trim(),
                Location =
                    string.IsNullOrWhiteSpace(dto.SuperUserLocation)
                        ? company.Location
                        : dto.SuperUserLocation.Trim()
            };

            var tempPassword =
                await _userService.GenerateTemporaryPasswordAsync();

            await _userService.SetPasswordHashAsync(
                user,
                tempPassword
            );

            await _db.Users.AddAsync(user);

            var protectedTemp =
                _protector.Protect(tempPassword);

            companySubscription.TempPasswordProtected =
                protectedTemp;

            await _db.CompanySubscriptions.AddAsync(
                companySubscription
            );

            await _db.SaveChangesAsync();

            if (sendEmail)
            {
                await SendOnboardingEmailAsync(
                    user,
                    company,
                    companySubscription,
                    tempPassword,
                    signinLinkBase
                );

                companySubscription.TempPasswordProtected = null;

                await _db.SaveChangesAsync();
            }

            return new OnboardResultDto
            {
                CompanyId = company.CompanyId,
                UserId = user.UserId,
                CompanySubscriptionId =
                    companySubscription.CompanySubscriptionId,
                AmountInCents =
                    (int)(
                        companySubscription.MonthlyPriceSnapshot * 100
                    )
            };
        }

        // ============================================================
        // FINALIZE ONBOARDING
        // ============================================================

        public async Task FinalizeOnboardPaymentAsync(
            Guid companyId,
            int userId,
            Guid companySubscriptionId,
            string signinLinkBase)
        {
            var companySubscription =
                await _db.CompanySubscriptions
                    .FirstOrDefaultAsync(cs =>
                        cs.CompanySubscriptionId ==
                            companySubscriptionId &&
                        cs.CompanyId == companyId);

            if (companySubscription == null)
            {
                throw new ArgumentException(
                    "Company subscription not found."
                );
            }

            if (companySubscription.IsPaid)
            {
                _logger.LogInformation(
                    "CompanySubscription {Id} already marked as paid; skipping.",
                    companySubscriptionId
                );

                return;
            }

            var user = await _db.Users
                .FirstOrDefaultAsync(u =>
                    u.UserId == userId &&
                    u.CompanyId == companyId);

            if (user == null)
                throw new ArgumentException(
                    "User not found for this company."
                );

            var company = await _db.Companies
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId);

            if (company == null)
                throw new ArgumentException(
                    "Company not found."
                );

            if (string.IsNullOrWhiteSpace(
                companySubscription.TempPasswordProtected))
            {
                _logger.LogError(
                    "No protected temp password found for CompanySubscription {Id}",
                    companySubscriptionId
                );

                throw new InvalidOperationException(
                    "Missing protected temp password."
                );
            }

            string tempPassword;

            try
            {
                tempPassword =
                    _protector.Unprotect(
                        companySubscription.TempPasswordProtected
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to unprotect temp password for CompanySubscription {Id}",
                    companySubscriptionId
                );

                throw;
            }

            await SendOnboardingEmailAsync(
                user,
                company,
                companySubscription,
                tempPassword,
                signinLinkBase
            );

            companySubscription.IsPaid = true;
            companySubscription.TempPasswordProtected = null;

            await _db.SaveChangesAsync();
        }

        // ============================================================
        // ONBOARDING EMAIL
        // ============================================================

        private async Task SendOnboardingEmailAsync(
            User user,
            Company company,
            CompanySubscription companySubscription,
            string tempPassword,
            string signinLinkBase)
        {
            var signInUrl = signinLinkBase;

            var encodedSignInUrl =
                System.Net.WebUtility.HtmlEncode(signInUrl);

            var encodedFirstName =
                System.Net.WebUtility.HtmlEncode(user.FirstName);

            var encodedTempPassword =
                System.Net.WebUtility.HtmlEncode(tempPassword);

            var companyNameEncoded =
                System.Net.WebUtility.HtmlEncode(
                    company.CompanyName
                );

            var logoUrl =
                _cfg.GetValue<string>("SendGrid:LogoUrl");

            var encodedLogoUrl =
                string.IsNullOrWhiteSpace(logoUrl)
                    ? ""
                    : System.Net.WebUtility.HtmlEncode(logoUrl);

            var subject =
                $"Welcome to {companyNameEncoded} — Account Created";

            var body = $@"
<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0""/>
<title>Welcome to {companyNameEncoded}</title>
</head>
<body style=""margin:0;padding:0;background-color:#f4f6f8;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;"">

<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color:#f4f6f8;padding:20px 0;"">
<tr>
<td align=""center"">

<table role=""presentation"" width=""600"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 4px 18px rgba(0,0,0,0.06);"">

<tr>
<td style=""padding:24px 28px;border-bottom:1px solid #eef2f5;background:linear-gradient(90deg,#0f6efd,#0b70d0);"">

<table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"">
<tr>

<td style=""vertical-align:middle;"">
{(string.IsNullOrWhiteSpace(encodedLogoUrl)
        ? $"<span style=\"color:#ffffff;font-weight:700;font-size:18px;\">{companyNameEncoded}</span>"
        : $"<img src=\"{encodedLogoUrl}\" alt=\"{companyNameEncoded} logo\" width=\"160\" style=\"display:block;border:0;max-width:160px;height:auto;\" />")}
</td>

<td align=""right"" style=""vertical-align:middle;color:#ffffff;font-size:14px;"">
<span style=""opacity:0.95;font-weight:600;"">
Welcome aboard
</span>
</td>

</tr>
</table>

</td>
</tr>

<tr>
<td style=""padding:28px 32px;color:#334155;"">

<h1 style=""margin:0 0 12px 0;font-size:20px;font-weight:700;color:#0f172a;"">
Hello {encodedFirstName},
</h1>

<p style=""margin:0 0 18px 0;color:#475569;line-height:1.6;font-size:15px;"">
Your account for <strong>{companyNameEncoded}</strong> has been created successfully.
Use the temporary password below to sign in — you'll be prompted to set a new password on first login.
</p>

<div style=""margin:16px 0 22px 0;padding:14px;border-radius:6px;background:#f8fafc;border:1px solid #e6eef8;font-family:'Courier New',Courier,monospace;color:#0f172a;font-size:16px;display:inline-block;"">
Temporary password:
<strong style=""margin-left:8px;"">
{encodedTempPassword}
</strong>
</div>

<div style=""margin:22px 0;text-align:left;"">
<a href=""{encodedSignInUrl}"" target=""_blank"" rel=""noopener noreferrer"" style=""display:inline-block;padding:12px 20px;border-radius:6px;background:#0f6efd;color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;"">
Sign in to your account
</a>
</div>

<p style=""margin:12px 0 8px 0;color:#64748b;font-size:13px;line-height:1.5;"">
If the button doesn't work, copy and paste the link below into your browser:
</p>

<p style=""word-break:break-all;font-size:13px;color:#0f172a;margin:0 0 18px 0;"">
<a href=""{encodedSignInUrl}"" target=""_blank"" rel=""noopener noreferrer"" style=""color:#0b69ff;text-decoration:underline;"">
{encodedSignInUrl}
</a>
</p>

<p style=""margin:0;color:#64748b;font-size:13px;line-height:1.5;"">
This temporary password will expire in <strong>48 hours</strong>.
If you did not request this account, please contact our support team immediately.
</p>

</td>
</tr>

<tr>
<td style=""padding:0 32px 18px 32px;"">
<hr style=""border:none;height:1px;background:#eef2f8;margin:0;"" />
</td>
</tr>

<tr>
<td style=""padding:14px 32px 28px 32px;font-size:13px;color:#94a3b8;"">

<p style=""margin:0 0 8px 0;"">
Need help?
Email us at
<a href=""mailto:support@hranalytix.com"" style=""color:#0b69ff;text-decoration:underline;"">
support@veiramal.com
</a>.
</p>

<p style=""margin:6px 0 0 0;font-size:12px;color:#94a3b8;"">
HrAnalytix — {companyNameEncoded}<br/>
123 Business Address, Floor 2, Sector X<br/>
xyz, Australia
</p>

<p style=""margin:12px 0 0 0;font-size:12px;color:#94a3b8;"">
You received this email because an account was created for you.
If you don’t want these emails,
<a href=""#"" style=""color:#0b69ff;text-decoration:underline;"">
unsubscribe
</a>.
</p>

</td>
</tr>

</table>

</td>
</tr>
</table>

</body>
</html>
";

            try
            {
                await _emailService.SendEmailAsync(
                    user.Email,
                    subject,
                    body
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send onboarding email to {Email} for CompanyId={CompanyId}",
                    user.Email,
                    company.CompanyId
                );
            }
        }

        // ============================================================
        // GET COMPANY
        // ============================================================

        public async Task<CompanyDto?> GetCompanyAsync(Guid companyId)
        {
            var c = await _db.Companies
                .FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId);

            if (c == null)
                return null;

            return new CompanyDto
            {
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyName,
                CompanyABN = c.CompanyABN,
                ContactNumber = c.ContactNumber,
                Location = c.Location,
                CreatedAt = c.CreatedAt,
                LogoUrl = c.LogoUrl
            };
        }

        // ============================================================
        // UPDATE COMPANY
        // ============================================================

        public async Task<CompanyDto> UpdateCompanyAsync(
            Guid companyId,
            CompanyUpdateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                throw new ArgumentException(
                    "CompanyName is required."
                );

            if (!string.IsNullOrWhiteSpace(dto.CompanyABN))
            {
                var abn = dto.CompanyABN.Trim();

                if (!AbnRegex.IsMatch(abn))
                {
                    throw new ArgumentException(
                        "Company ABN must be exactly 11 digits."
                    );
                }
            }

            var company = await _db.Companies
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == companyId);

            if (company == null)
                throw new ArgumentException(
                    "Company not found."
                );

            company.CompanyName =
                dto.CompanyName.Trim();

            company.CompanyABN =
                string.IsNullOrWhiteSpace(dto.CompanyABN)
                    ? null
                    : dto.CompanyABN.Trim();

            company.ContactNumber =
                string.IsNullOrWhiteSpace(dto.ContactNumber)
                    ? null
                    : dto.ContactNumber.Trim();

            company.Location =
                string.IsNullOrWhiteSpace(dto.Location)
                    ? null
                    : dto.Location.Trim();

            _db.Companies.Update(company);

            await _db.SaveChangesAsync();

            return new CompanyDto
            {
                CompanyId = company.CompanyId,
                CompanyName = company.CompanyName,
                CompanyABN = company.CompanyABN,
                ContactNumber = company.ContactNumber,
                Location = company.Location,
                CreatedAt = company.CreatedAt,
                LogoUrl = company.LogoUrl
            };
        }

        // ============================================================
        // CREATE SUBCOMPANY
        // ============================================================

        public async Task<SubCompanyDto> CreateSubCompanyAsync(
            Guid parentCompanyId,
            CreateSubCompanyDto dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                throw new ArgumentException(
                    "CompanyName is required."
                );

            var parentCompany = await _db.Companies
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == parentCompanyId);

            if (parentCompany == null)
                throw new ArgumentException(
                    "Parent company not found."
                );

            var companySubscription =
                await _db.CompanySubscriptions
                    .Where(cs =>
                        cs.CompanyId == parentCompanyId)
                    .OrderByDescending(cs =>
                        cs.StartDate)
                    .Include(cs =>
                        cs.SubscriptionPlan)
                    .FirstOrDefaultAsync();

            if (companySubscription == null)
            {
                throw new InvalidOperationException(
                    "Parent company does not have an active subscription."
                );
            }

            var plan = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(p =>
                    p.SubscriptionPlanId ==
                    companySubscription.SubscriptionPlanId);

            if (plan == null)
            {
                throw new InvalidOperationException(
                    "Parent company subscription plan not found."
                );
            }

            if (plan.SubscriptionPlanId != EnterprisePlusPlanId &&
                !string.Equals(
                    plan.Package,
                    "Enterprise Plus",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Only companies on the Enterprise Plus plan can create subcompanies."
                );
            }

            var subCompany = new Company
            {
                CompanyId = Guid.NewGuid(),
                CompanyName = dto.CompanyName.Trim(),
                CompanyABN =
                    string.IsNullOrWhiteSpace(dto.CompanyABN)
                        ? null
                        : dto.CompanyABN.Trim(),
                ContactNumber =
                    string.IsNullOrWhiteSpace(dto.ContactNumber)
                        ? null
                        : dto.ContactNumber.Trim(),
                Location =
                    string.IsNullOrWhiteSpace(dto.Location)
                        ? null
                        : dto.Location.Trim(),
                ParentCompanyId = parentCompanyId,
                CreatedAt = DateTime.UtcNow
            };

            await _db.Companies.AddAsync(subCompany);

            // Remove duplicate IDs from incoming request
            var requestedUserIds =
                (dto.AssignedSuperUserIds ??
                 Array.Empty<int>())
                .Distinct()
                .ToArray();

            var validParentSuperUsers =
                Array.Empty<int>();

            if (requestedUserIds.Length > 0)
            {
                validParentSuperUsers =
                    await _db.Users
                        .Where(u =>
                            u.CompanyId == parentCompanyId &&
                            u.AccessLevel == "superUser" &&
                            u.IsActive &&
                            requestedUserIds.Contains(u.UserId))
                        .Select(u => u.UserId)
                        .ToArrayAsync();

                var missing =
                    requestedUserIds
                        .Except(validParentSuperUsers)
                        .ToArray();

                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"One or more assigned users are not valid superusers of the parent company: {string.Join(',', missing)}"
                    );
                }

                foreach (var uid in validParentSuperUsers.Distinct())
                {
                    await _db.CompanySuperUserAssignments.AddAsync(
                        new CompanySuperUserAssignment
                        {
                            CompanyId = subCompany.CompanyId,
                            UserId = uid
                        }
                    );
                }
            }

            await _db.SaveChangesAsync();

            return new SubCompanyDto
            {
                CompanyId = subCompany.CompanyId,
                CompanyName = subCompany.CompanyName,
                CompanyABN = subCompany.CompanyABN,
                ContactNumber = subCompany.ContactNumber,
                Location = subCompany.Location,
                ParentCompanyId = parentCompanyId,
                CreatedAt = subCompany.CreatedAt,
                AssignedSuperUserIds =
                    validParentSuperUsers
                        .Distinct()
                        .ToArray()
            };
        }

        // ============================================================
        // LIST SUBCOMPANIES
        // ============================================================

        public async Task<List<SubCompanyDto>> ListSubCompaniesAsync(
            Guid parentCompanyId)
        {
            var children = await _db.Companies
                .Where(c =>
                    c.ParentCompanyId == parentCompanyId)
                .OrderBy(c => c.CompanyName)
                .ToListAsync();

            var companyIds = children
                .Select(c => c.CompanyId)
                .ToList();

            var assignments = await _db.CompanySuperUserAssignments
                .Where(a =>
                    companyIds.Contains(a.CompanyId))
                .Select(a => new
                {
                    a.CompanyId,
                    a.UserId
                })
                .ToListAsync();

            return children
                .Select(c => new SubCompanyDto
                {
                    CompanyId = c.CompanyId,
                    CompanyName = c.CompanyName,
                    CompanyABN = c.CompanyABN,
                    ContactNumber = c.ContactNumber,
                    Location = c.Location,
                    ParentCompanyId = parentCompanyId,
                    CreatedAt = c.CreatedAt,

                    AssignedSuperUserIds =
                        assignments
                            .Where(a =>
                                a.CompanyId == c.CompanyId)
                            .Select(a => a.UserId)
                            .Distinct()
                            .ToArray()
                })
                .ToList();
        }

        // ============================================================
        // GET PARENT COMPANY SUPERUSERS
        // ============================================================

        public async Task<List<User>>
            GetParentCompanySuperUsersAsync(
                Guid parentCompanyId)
        {
            return await _db.Users
                .Where(u =>
                    u.CompanyId == parentCompanyId &&
                    u.AccessLevel == "superUser" &&
                    u.IsActive)
                .OrderBy(u => u.EmployeeNumber)
                .ToListAsync();
        }

        // ============================================================
        // ASSIGN SUPERUSERS TO SUBCOMPANY
        // ============================================================

        public async Task AssignSuperUsersToSubCompanyAsync(
            Guid parentCompanyId,
            Guid subCompanyId,
            int[] userIds,
            bool replaceExisting = true)
        {
            var subCompany = await _db.Companies
                .FirstOrDefaultAsync(c =>
                    c.CompanyId == subCompanyId);

            if (subCompany == null)
            {
                throw new ArgumentException(
                    "Subcompany not found."
                );
            }

            if (subCompany.ParentCompanyId != parentCompanyId)
            {
                throw new InvalidOperationException(
                    "Subcompany does not belong to the given parent company."
                );
            }

            // Clean incoming IDs
            userIds = (userIds ?? Array.Empty<int>())
                .Distinct()
                .ToArray();

            // Validate that every selected user is:
            // - in the parent company
            // - a Superuser
            // - active
            var validParentSuperUsers =
                await _db.Users
                    .Where(u =>
                        u.CompanyId == parentCompanyId &&
                        u.AccessLevel == "superUser" &&
                        u.IsActive &&
                        userIds.Contains(u.UserId))
                    .Select(u => u.UserId)
                    .ToListAsync();

            var invalid =
                userIds
                    .Except(validParentSuperUsers)
                    .ToArray();

            if (invalid.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Some users are not valid parent-company superusers: {string.Join(',', invalid)}"
                );
            }

            if (replaceExisting)
            {
                var existingAssignments =
                    await _db.CompanySuperUserAssignments
                        .Where(a =>
                            a.CompanyId == subCompanyId)
                        .ToListAsync();

                _db.CompanySuperUserAssignments
                    .RemoveRange(existingAssignments);
            }

            foreach (var uid in validParentSuperUsers.Distinct())
            {
                _db.CompanySuperUserAssignments.Add(
                    new CompanySuperUserAssignment
                    {
                        CompanyId = subCompanyId,
                        UserId = uid
                    }
                );
            }

            await _db.SaveChangesAsync();
        }
    }
}