using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml; // EPPlus
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly AppDbContext _db;
        private readonly IUserService _userService;
        private readonly IEmailService _email;
        public UserManagementService(AppDbContext db, IUserService userService, IEmailService email)
        {
            _db = db;
            _userService = userService;
            _email = email;
        }

        private int NextEmployeeNumber(Guid companyId)
        {
            var max = _db.Users.Where(u => u.CompanyId == companyId).Max(u => (int?)u.EmployeeNumber) ?? 0;
            return max + 1;
        }

        public async Task<(User? user, string tempPassword)> CreateUserAsync(Guid companyId, CreateUserDto dto)
        {
            // Ensure company exists
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            if (company == null) return (null, string.Empty);

            // NEW: Prevent creating superUser in subcompanies
            if (company.ParentCompanyId.HasValue && string.Equals(dto.AccessLevel, "superUser", StringComparison.OrdinalIgnoreCase))
            {
                // Not allowed for subcompany
                throw new InvalidOperationException("Cannot create a user with AccessLevel 'superUser' for a subcompany.");
            }
            // check duplicate by email in the company
            var exists = await _db.Users.AnyAsync(u => u.CompanyId == companyId && u.Email.ToLower() == dto.Email.ToLower());
            if (exists) return (null, string.Empty);

            var user = new User
            {
                CompanyId = companyId,
                FirstName = dto.FirstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(dto.MiddleName) ? null : dto.MiddleName.Trim(),
                LastName = string.IsNullOrWhiteSpace(dto.LastName) ? null : dto.LastName.Trim(),
                Email = dto.Email.Trim(),
                BusinessUnit = dto.BusinessUnit,
                AccessLevel = string.IsNullOrWhiteSpace(dto.AccessLevel) ? "employee" : dto.AccessLevel,
                ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber) ? null : dto.ContactNumber.Trim(),
                Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim(),
                IsPasswordResetRequired = dto.ForcePasswordReset,
                IsActive = true,
                IsFirstLogin = true, 
                EmployeeNumber = NextEmployeeNumber(companyId),
                CreatedAt = DateTime.UtcNow
            };

            var tempPassword = await _userService.GenerateTemporaryPasswordAsync();
            await _userService.SetPasswordHashAsync(user, tempPassword);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // send email
            var subject = "Account created - temporary password";
            var html = BuildNewUserEmail(user, tempPassword);
            await _email.SendEmailAsync(user.Email, subject, html);

            return (user, tempPassword);
        }


        private string BuildNewUserEmail(User user, string tempPassword)
        {
            // Simple HTML template — adjust to your front end
            return $@"
                <p>Hi {user.FirstName},</p>
                <p>An account has been created for you at <strong>{user.CompanyId}</strong>.</p>
                <p><strong>Email:</strong> {user.Email}</p>
                <p><strong>Temporary password:</strong> {tempPassword}</p>
                <p>Please log in and <strong>reset your password</strong> when prompted.</p>
                <p>Regards,<br/>Your Team</p>";
        }

        public async Task<BulkUploadResultDto> CreateUsersFromExcelAsync(Guid companyId, Stream excelStream, string uploaderEmail)
        {
            var result = new BulkUploadResultDto();

            using var pkg = new ExcelPackage(excelStream);
            var ws = pkg.Workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                result.Errors.Add("No worksheet found in uploaded file.");
                return result;
            }

            var rowCount = ws.Dimension?.Rows ?? 0;
            var colCount = ws.Dimension?.Columns ?? 0;
            if (rowCount < 2)
            {
                result.Errors.Add("Worksheet has no data rows.");
                return result;
            }

            // Read header row (row 1) and build a dictionary headerName -> columnIndex
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= colCount; col++)
            {
                var rawHeader = ws.Cells[1, col].GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(rawHeader)) continue;

                // normalize header: remove spaces/underscores/lowercase to support many variants
                var normalized = rawHeader.Replace(" ", "").Replace("_", "").ToLowerInvariant();

                if (!headerMap.ContainsKey(normalized))
                    headerMap[normalized] = col;
            }

            // Helper: get cell value by possible header keys
            string? ReadCell(int row, params string[] possibleKeys)
            {
                foreach (var key in possibleKeys)
                {
                    var norm = key.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                    if (headerMap.TryGetValue(norm, out var c))
                    {
                        var val = ws.Cells[row, c].GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                    }
                }
                return null;
            }

            // Define common header keys for required/optional columns
            var firstNameKeys = new[] { "firstname", "first name", "first_name" };
            var middleNameKeys = new[] { "middlename", "middle name", "middle_name" };
            var lastNameKeys = new[] { "lastname", "last name", "last_name" };
            var emailKeys = new[] { "email", "emailaddress", "email_address" };
            var businessUnitKeys = new[] { "businessunit", "business unit", "business_unit", "department" };
            var accessLevelKeys = new[] { "accesslevel", "access level", "access_level", "role" };
            var contactKeys = new[] { "contactnumber", "contact number", "contact_number", "phone", "phonenumber", "phone_number" };
            var locationKeys = new[] { "location", "address", "site", "office" };

            for (int row = 2; row <= rowCount; row++)
            {
                try
                {
                    var firstName = ReadCell(row, firstNameKeys) ?? string.Empty;
                    var middleName = ReadCell(row, middleNameKeys);
                    var lastName = ReadCell(row, lastNameKeys);
                    var email = ReadCell(row, emailKeys) ?? string.Empty;
                    var businessUnit = ReadCell(row, businessUnitKeys);
                    var accessLevel = ReadCell(row, accessLevelKeys);
                    var contactNumber = ReadCell(row, contactKeys);
                    var location = ReadCell(row, locationKeys);

                    if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(email))
                    {
                        result.SkippedCount++;
                        result.Errors.Add($"Row {row}: FirstName or Email missing - skipped.");
                        continue;
                    }

                    // Skip duplicates by email
                    var exists = await _db.Users.AnyAsync(u => u.CompanyId == companyId && u.Email.ToLower() == email.ToLower());
                    if (exists)
                    {
                        result.SkippedCount++;
                        continue;
                    }

                    var dto = new CreateUserDto
                    {
                        FirstName = firstName,
                        MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName,
                        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName,
                        Email = email,
                        BusinessUnit = businessUnit,
                        AccessLevel = string.IsNullOrWhiteSpace(accessLevel) ? "employee" : accessLevel,
                        ContactNumber = string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber,
                        Location = string.IsNullOrWhiteSpace(location) ? null : location,
                        ForcePasswordReset = true
                    };

                    var (user, tempPassword) = await CreateUserAsync(companyId, dto);

                    if (user == null)
                    {
                        result.SkippedCount++;
                        result.Errors.Add($"Row {row}: Could not create user {email} (likely duplicate).");
                        continue;
                    }

                    result.CreatedCount++;
                }
                catch (Exception ex)
                {
                    result.SkippedCount++;
                    result.Errors.Add($"Row {row}: Exception - {ex.Message}");
                }
            }

            return result;
        }


        public async Task<List<User>> ListUsersAsync(Guid companyId)
        {
            // Find the company (sub or parent)
            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == companyId);

            if (company == null)
            {
                return new List<User>();
            }

            // If this is a parent company (no ParentCompanyId) -> return users belonging to it
            if (!company.ParentCompanyId.HasValue)
            {
                return await _db.Users
                    .Where(u => u.CompanyId == companyId)
                    .OrderBy(u => u.EmployeeNumber)
                    .ToListAsync();
            }

            // This is a subcompany: include:
            //  - users whose CompanyId == subcompany
            //  - users from the parent company who are assigned to this subcompany
            var parentId = company.ParentCompanyId.Value;

            // get userIds that are assigned to this subcompany
            var assignedUserIds = await _db.CompanySuperUserAssignments
                .Where(a => a.CompanyId == companyId)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync();

            var list = await _db.Users
                .Where(u =>
                    u.CompanyId == companyId
                    || (u.CompanyId == parentId && assignedUserIds.Contains(u.UserId))
                )
                .OrderBy(u => u.EmployeeNumber)
                .ToListAsync();

            return list;
        }


        public async Task<User?> UpdateUserAsync(Guid companyId, UpdateUserDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.UserId == dto.UserId);
            if (user == null) return null;

            // check email collision with other users
            var exists = await _db.Users.AnyAsync(u => u.CompanyId == companyId && u.Email.ToLower() == dto.Email.ToLower() && u.UserId != dto.UserId);
            if (exists) throw new InvalidOperationException("Email already in use by another user.");

            user.FirstName = dto.FirstName.Trim();
            user.MiddleName = string.IsNullOrWhiteSpace(dto.MiddleName) ? null : dto.MiddleName.Trim(); // NEW
            user.LastName = string.IsNullOrWhiteSpace(dto.LastName) ? null : dto.LastName.Trim();
            user.Email = dto.Email.Trim();
            user.BusinessUnit = dto.BusinessUnit;
            user.AccessLevel = dto.AccessLevel ?? user.AccessLevel;
            user.IsActive = dto.IsActive;

            // NEW fields
            user.ContactNumber = string.IsNullOrWhiteSpace(dto.ContactNumber) ? null : dto.ContactNumber.Trim();
            user.Location = string.IsNullOrWhiteSpace(dto.Location) ? null : dto.Location.Trim();

            await _db.SaveChangesAsync();
            return user;
        }


        public async Task<bool> InactivateUserAsync(Guid companyId, int userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.UserId == userId);
            if (user == null) return false;
            user.IsActive = false;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ActivateUserAsync(Guid companyId, int userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.CompanyId == companyId && u.UserId == userId);
            if (user == null) return false;

            user.IsActive = true;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
