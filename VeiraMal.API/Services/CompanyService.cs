using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
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

        // ABN must be exactly 11 digits (only digits)
        private static readonly Regex AbnRegex = new Regex(@"^\d{11}$", RegexOptions.Compiled);
        private static readonly Guid EnterprisePlusPlanId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        public CompanyService(
            AppDbContext db,
            IUserService userService,
            IEmailService emailService,
            IConfiguration cfg,
            ILogger<CompanyService> logger)
        {
            _db = db;
            _userService = userService;
            _emailService = emailService;
            _cfg = cfg;
            _logger = logger;
        }

        /// <summary>
        /// Onboards a company and creates the superuser; returns CompanyId (Guid).
        /// Validates ABN is exactly 11 digits; throws ArgumentException if invalid.
        /// </summary>
        public async Task<Guid> OnboardCompanyAsync(CompanyOnboardDto dto, string signinLinkBase)
        {
            // Validate required fields (basic)
            if (string.IsNullOrWhiteSpace(dto.SuperUserEmail))
                throw new ArgumentException("SuperUserEmail is required.");
            if (string.IsNullOrWhiteSpace(dto.SuperUserFirstName))
                throw new ArgumentException("SuperUserFirstName is required.");
            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                throw new ArgumentException("CompanyName is required.");

            // Validate ABN if provided
            if (!string.IsNullOrWhiteSpace(dto.CompanyABN))
            {
                var abn = dto.CompanyABN!.Trim();
                if (!AbnRegex.IsMatch(abn))
                {
                    throw new ArgumentException("Company ABN must be exactly 11 digits.");
                }
            }

            // Validate plan exists
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.SubscriptionPlanId == dto.SubscriptionPlanId);
            if (plan == null)
            {
                throw new ArgumentException("Subscription plan not found. Please choose a valid plan.");
            }

            // Validate additional seats
            if (dto.AdditionalSeatsRequested < 0)
                throw new ArgumentException("AdditionalSeatsRequested cannot be negative.");

            if (!plan.AdditionalSeatsAllowed && dto.AdditionalSeatsRequested > 0)
            {
                throw new ArgumentException("This plan does not allow purchasing additional seats at signup. Contact sales.");
            }

            // Validate against MaxHC if present
            if (plan.MaxHC.HasValue)
            {
                var totalRequested = (plan.BaseUserSeats == 0 ? int.MaxValue : plan.BaseUserSeats) + dto.AdditionalSeatsRequested;
                if (plan.BaseUserSeats != 0 && totalRequested > plan.MaxHC.Value)
                {
                    throw new ArgumentException($"Requested seats exceed plan's HC cap of {plan.MaxHC.Value}.");
                }
            }

            // If plan price is null or 0 for custom pricing, route to sales (adjust as desired)
            if (!plan.PricePerMonth.HasValue || plan.PricePerMonth.Value == 0m)
            {
                throw new ArgumentException("Selected plan requires a custom contract. Please contact sales to complete onboarding.");
            }

            // Create company (persist Location too)
            var company = new Company
            {
                CompanyId = Guid.NewGuid(),
                CompanyName = dto.CompanyName.Trim(),
                CompanyABN = string.IsNullOrWhiteSpace(dto.CompanyABN) ? null : dto.CompanyABN.Trim(),
                ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber) ? null : dto.ContactNumber.Trim(),
                Location = string.IsNullOrWhiteSpace(dto.CompanyLocation) ? null : dto.CompanyLocation.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            await _db.Companies.AddAsync(company);
            await _db.SaveChangesAsync(); // ensure CompanyId is persisted for FK usage

            // Compute price snapshot
            var additionalPrice = plan.AdditionalSeatPrice ?? 0m;
            var additionalCost = additionalPrice * dto.AdditionalSeatsRequested;
            var monthlyPrice = (plan.PricePerMonth ?? 0m) + additionalCost;

            // Create company subscription snapshot
            var companySubscription = new CompanySubscription
            {
                CompanyId = company.CompanyId,
                SubscriptionPlanId = plan.SubscriptionPlanId,
                PlanNameSnapshot = plan.Package,
                BaseUserSeatsSnapshot = plan.BaseUserSeats,
                AdditionalSeatsPurchased = dto.AdditionalSeatsRequested,
                AdditionalSeatPriceSnapshot = additionalPrice,
                MonthlyPriceSnapshot = monthlyPrice,
                StartDate = DateTime.UtcNow
            };

            await _db.CompanySubscriptions.AddAsync(companySubscription);

            // Create superuser and populate new fields:
            // If SuperUserContactNumber is empty, default to company.ContactNumber.
            // If SuperUserLocation is empty, default to company.Location.
            var user = new User
            {
                CompanyId = company.CompanyId,
                EmployeeNumber = 1,
                FirstName = dto.SuperUserFirstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(dto.SuperUserMiddleName) ? null : dto.SuperUserMiddleName.Trim(),
                LastName = string.IsNullOrWhiteSpace(dto.SuperUserLastName) ? null : dto.SuperUserLastName.Trim(),
                Email = dto.SuperUserEmail.Trim(),
                BusinessUnit = "Management",
                AccessLevel = "superUser",
                IsPasswordResetRequired = true,
                IsFirstLogin = true,
                ContactNumber = string.IsNullOrWhiteSpace(dto.SuperUserContactNumber) ? company.ContactNumber : dto.SuperUserContactNumber.Trim(),
                Location = string.IsNullOrWhiteSpace(dto.SuperUserLocation) ? company.Location : dto.SuperUserLocation.Trim()
            };

            // generate temp password and hash
            var tempPassword = await _userService.GenerateTemporaryPasswordAsync();
            await _userService.SetPasswordHashAsync(user, tempPassword);

            await _db.Users.AddAsync(user);

            await _db.SaveChangesAsync();

            // Prepare email content (unchanged)
            var signInUrl = signinLinkBase;
            var subject = $"Welcome to {company.CompanyName} — Account Created";
            var body = $@"
            <p>Hello {user.FirstName},</p>
            <p>Your account has been created. Use the temporary password below to sign in and you will be prompted to set a new password:</p>
            <p><b>Temporary password:</b> {System.Net.WebUtility.HtmlEncode(tempPassword)}</p>
            <p><a href='{signInUrl}'>Sign in</a></p>
            <p>This temporary password will expire after 48 hours. If you did not request this, contact support.</p>
            <p>Thanks,<br/>Your App Team</p>
            ";

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send onboarding email to {Email} for CompanyId={CompanyId}", user.Email, company.CompanyId);
            }

            return company.CompanyId;
        }



        /// <summary>
        /// Returns company details or null if not found.
        /// </summary>
        public async Task<CompanyDto?> GetCompanyAsync(Guid companyId)
        {
            var c = await _db.Companies.FirstOrDefaultAsync(x => x.CompanyId == companyId);
            if (c == null) return null;

            return new CompanyDto
            {
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyName,
                CompanyABN = c.CompanyABN,
                ContactNumber = c.ContactNumber,
                Location = c.Location,     // <--- include Location
                CreatedAt = c.CreatedAt
            };
        }

        /// <summary>
        /// Updates company details. Validates ABN format. Throws ArgumentException on validation error.
        /// Returns updated CompanyDto.
        /// </summary>
        public async Task<CompanyDto> UpdateCompanyAsync(Guid companyId, CompanyUpdateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CompanyName))
                throw new ArgumentException("CompanyName is required.");

            if (!string.IsNullOrWhiteSpace(dto.CompanyABN))
            {
                var abn = dto.CompanyABN!.Trim();
                if (!AbnRegex.IsMatch(abn))
                    throw new ArgumentException("Company ABN must be exactly 11 digits.");
            }

            var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company == null)
                throw new ArgumentException("Company not found.");

            company.CompanyName = dto.CompanyName.Trim();
            company.CompanyABN = string.IsNullOrWhiteSpace(dto.CompanyABN) ? null : dto.CompanyABN!.Trim();
            company.ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber) ? null : dto.ContactNumber!.Trim();

            // NEW: persist Location from DTO
            company.Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location!.Trim();

            _db.Companies.Update(company);
            await _db.SaveChangesAsync();

            return new CompanyDto
            {
                CompanyId = company.CompanyId,
                CompanyName = company.CompanyName,
                CompanyABN = company.CompanyABN,
                ContactNumber = company.ContactNumber,
                Location = company.Location, // <--- return it to client
                CreatedAt = company.CreatedAt
            };
        }

        /// <summary>
        /// Create a subcompany under parentCompanyId.
        /// Only companies with Enterprise Plus plan are allowed to create subcompanies.
        /// </summary>
        public async Task<SubCompanyDto> CreateSubCompanyAsync(Guid parentCompanyId, CreateSubCompanyDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.CompanyName)) throw new ArgumentException("CompanyName is required.");

            // Verify parent company exists
            var parentCompany = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == parentCompanyId);
            if (parentCompany == null) throw new ArgumentException("Parent company not found.");

            // Verify parent's active subscription plan allows subcompanies (Enterprise Plus only)
            var companySubscription = await _db.CompanySubscriptions
                .Where(cs => cs.CompanyId == parentCompanyId)
                .OrderByDescending(cs => cs.StartDate) // latest
                .Include(cs => cs.SubscriptionPlan)
                .FirstOrDefaultAsync();

            if (companySubscription == null) throw new InvalidOperationException("Parent company does not have an active subscription.");

            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.SubscriptionPlanId == companySubscription.SubscriptionPlanId);
            if (plan == null) throw new InvalidOperationException("Parent company subscription plan not found.");

            // Only Enterprise Plus allowed to create subcompanies
            // check by GUID to be strict: Enterprise Plus GUID = 5555...
            if (plan.SubscriptionPlanId != EnterprisePlusPlanId && !string.Equals(plan.Package, "Enterprise Plus", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only companies on the Enterprise Plus plan can create subcompanies.");
            }

            // Create the subcompany (persist ParentCompanyId)
            var subCompany = new Company
            {
                CompanyId = Guid.NewGuid(),
                CompanyName = dto.CompanyName.Trim(),
                CompanyABN = string.IsNullOrWhiteSpace(dto.CompanyABN) ? null : dto.CompanyABN.Trim(),
                ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber) ? null : dto.ContactNumber.Trim(),
                Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim(),
                ParentCompanyId = parentCompanyId,
                CreatedAt = DateTime.UtcNow
            };

            await _db.Companies.AddAsync(subCompany);

            // Validate assigned superuser ids (must be superusers from parent company)
            if (dto.AssignedSuperUserIds != null && dto.AssignedSuperUserIds.Length > 0)
            {
                // Fetch those users and validate
                var parentSuperUsers = await _db.Users
                    .Where(u => u.CompanyId == parentCompanyId && u.AccessLevel == "superUser" && dto.AssignedSuperUserIds.Contains(u.UserId))
                    .Select(u => u.UserId)
                    .ToListAsync();

                // If any provided Ids not found/valid, ignore or throw (here we throw to notify caller)
                var missing = dto.AssignedSuperUserIds.Except(parentSuperUsers).ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException($"One or more assigned users are not valid superusers of the parent company: {string.Join(',', missing)}");
                }

                // add assignments
                foreach (var uid in parentSuperUsers)
                {
                    var assign = new CompanySuperUserAssignment
                    {
                        CompanyId = subCompany.CompanyId,
                        UserId = uid
                    };
                    await _db.CompanySuperUserAssignments.AddAsync(assign);
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
                CreatedAt = subCompany.CreatedAt
            };
        }

        public async Task<List<SubCompanyDto>> ListSubCompaniesAsync(Guid parentCompanyId)
        {
            var children = await _db.Companies
                .Where(c => c.ParentCompanyId == parentCompanyId)
                .OrderBy(c => c.CompanyName)
                .ToListAsync();

            return children.Select(c => new SubCompanyDto
            {
                CompanyId = c.CompanyId,
                CompanyName = c.CompanyName,
                CompanyABN = c.CompanyABN,
                ContactNumber = c.ContactNumber,
                Location = c.Location,
                ParentCompanyId = parentCompanyId,
                CreatedAt = c.CreatedAt
            }).ToList();
        }

        /// <summary>
        /// Returns list of users in parent company that have AccessLevel = "superUser".
        /// Useful to populate dropdown.
        /// </summary>
        public async Task<List<User>> GetParentCompanySuperUsersAsync(Guid parentCompanyId)
        {
            return await _db.Users
                .Where(u => u.CompanyId == parentCompanyId && u.AccessLevel == "superUser" && u.IsActive)
                .OrderBy(u => u.EmployeeNumber)
                .ToListAsync();
        }

        /// <summary>
        /// Assigns the given parent-company user ids as managers for the subcompany.
        /// This will replace existing assignments if replaceExisting==true, otherwise it will upsert.
        /// </summary>
        public async Task AssignSuperUsersToSubCompanyAsync(Guid parentCompanyId, Guid subCompanyId, int[] userIds, bool replaceExisting = true)
        {
            // Ensure subcompany belongs to parentCompanyId
            var subCompany = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == subCompanyId);
            if (subCompany == null) throw new ArgumentException("Subcompany not found.");
            if (subCompany.ParentCompanyId != parentCompanyId) throw new InvalidOperationException("Subcompany does not belong to the given parent company.");

            // Validate users are actual superusers for the parent company
            var validParentSuperUsers = await _db.Users
                .Where(u => u.CompanyId == parentCompanyId && u.AccessLevel == "superUser" && userIds.Contains(u.UserId))
                .Select(u => u.UserId)
                .ToListAsync();

            var invalid = userIds.Except(validParentSuperUsers).ToArray();
            if (invalid.Length > 0)
                throw new InvalidOperationException($"Some users are not valid parent-company superusers: {string.Join(',', invalid)}");

            if (replaceExisting)
            {
                var existing = _db.CompanySuperUserAssignments.Where(a => a.CompanyId == subCompanyId);
                _db.CompanySuperUserAssignments.RemoveRange(existing);
            }

            foreach (var uid in validParentSuperUsers)
            {
                _db.CompanySuperUserAssignments.Add(new CompanySuperUserAssignment
                {
                    CompanyId = subCompanyId,
                    UserId = uid
                });
            }

            await _db.SaveChangesAsync();
        }
    }
}
