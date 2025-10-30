using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class HeadcountService : IHeadcountService
    {
        private readonly AppDbContext _context;

        public HeadcountService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> UploadAsync(IFormFile file)
        {
            var list = await ParseExcelFileAsync(file);

            // Remove all existing headcount records
            _context.Headcounts.RemoveRange(_context.Headcounts);
            await _context.SaveChangesAsync();

            // Insert the new list
            await _context.Headcounts.AddRangeAsync(list);
            await _context.SaveChangesAsync();

            return $"{list.Count} headcount records successfully uploaded (old data replaced)";
        }


        public async Task<IEnumerable<Headcount>> GetAllAsync()
        {
            return await _context.Headcounts.AsNoTracking().ToListAsync();
        }

        public async Task<IEnumerable<object>> GetAnalysisAsync()
        {
            var allEmployees = await _context.Headcounts.AsNoTracking().ToListAsync();

            return allEmployees
                .GroupBy(e => e.OrganizationalKey)
                .Select(g =>
                {
                    var totalInDept = g.Count();
                    var tempCount = g.Count(e => e.Status?.Contains("Temporary", StringComparison.OrdinalIgnoreCase) ?? false);
                    var permanentCount = totalInDept - tempCount;
                    var maleCount = g.Count(e => e.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) ?? false);
                    var femaleCount = g.Count(e => e.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) ?? false);

                    var tenureSum = 0;
                    var tenureCount = 0;
                    foreach (var emp in g)
                    {
                        if (int.TryParse(emp.Tenure, out int tenure))
                        {
                            tenureSum += tenure;
                            tenureCount++;
                        }
                    }
                    var avgTenure = tenureCount > 0 ? tenureSum / (double)tenureCount : 0;

                    return new
                    {
                        Department = g.Key ?? "Unknown",
                        Headcount = totalInDept,
                        HeadcountPercentage = Math.Round(permanentCount * 100.0 / totalInDept, 2),
                        TempPercentage = Math.Round(tempCount * 100.0 / totalInDept, 2),
                        MaleCount = maleCount,
                        MalePercentage = Math.Round(maleCount * 100.0 / totalInDept, 2),
                        FemaleCount = femaleCount,
                        FemalePercentage = Math.Round(femaleCount * 100.0 / totalInDept, 2),
                        TempCount = tempCount,
                        AverageAge = Math.Round(g.Average(e => e.AgeOfEmployee), 2),
                        AverageTenure = Math.Round(avgTenure, 2)
                    };
                })
                .OrderByDescending(d => d.Headcount)
                .ToList();
        }

        public async Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month, string organizationalKey = "Finance")
        {
            // Normalize inputs and provide sensible defaults
            var monthNorm = (month ?? "").Trim();
            if (string.IsNullOrEmpty(monthNorm))
                monthNorm = DateTime.UtcNow.ToString("MMMM"); // default to current month name

            var orgKeyToFilter = string.IsNullOrWhiteSpace(organizationalKey) ? "Finance" : organizationalKey.Trim();

            // Lowercase both sides for case-insensitive comparison (EF Core translates ToLower to SQL)
            var monthLower = monthNorm.ToLower();
            var orgKeyLower = orgKeyToFilter.ToLower();

            var financeEmployees = await _context.Headcounts
                .AsNoTracking()
                .Where(e => ((e.OrganizationalKey ?? "").ToLower() == orgKeyLower)
                         && ((e.Month ?? "").ToLower() == monthLower))
                .ToListAsync();

            return financeEmployees
                .GroupBy(e => e.OrganizationalUnit)  // Group by unit within the specified org key
                .Select(g =>
                {
                    var totalInUnit = g.Count();
                    var tempCount = g.Count(e => e.Status?.Contains("Temporary", StringComparison.OrdinalIgnoreCase) ?? false);
                    var permanentCount = totalInUnit - tempCount;

                    var maleCount = g.Count(e => e.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) ?? false);
                    var femaleCount = g.Count(e => e.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) ?? false);

                    // tenure calc
                    var tenureSum = 0;
                    var tenureCount = 0;
                    foreach (var emp in g)
                    {
                        if (int.TryParse(emp.Tenure, out int tenure))
                        {
                            tenureSum += tenure;
                            tenureCount++;
                        }
                    }
                    var avgTenure = tenureCount > 0 ? tenureSum / (double)tenureCount : 0;

                    return new
                    {
                        OrganizationalUnit = g.Key ?? "Unknown",
                        Headcount = totalInUnit,
                        HeadcountPercentage = totalInUnit > 0 ? Math.Round(permanentCount * 100.0 / totalInUnit, 2) : 0,
                        TempPercentage = totalInUnit > 0 ? Math.Round(tempCount * 100.0 / totalInUnit, 2) : 0,

                        MaleCount = maleCount,
                        MalePercentage = totalInUnit > 0 ? Math.Round(maleCount * 100.0 / totalInUnit, 2) : 0,

                        FemaleCount = femaleCount,
                        FemalePercentage = totalInUnit > 0 ? Math.Round(femaleCount * 100.0 / totalInUnit, 2) : 0,

                        TempCount = tempCount,
                        AverageAge = totalInUnit > 0 ? Math.Round(g.Average(e => e.AgeOfEmployee), 2) : 0,
                        AverageTenure = Math.Round(avgTenure, 2)
                    };
                })
                .OrderByDescending(d => d.Headcount)
                .ToList();
        }





        private async Task<List<Headcount>> ParseExcelFileAsync(IFormFile file)
        {
            var headcountList = new List<Headcount>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets[0];
            int rowCount = worksheet.Dimension?.Rows ?? 0;

            for (int row = 2; row <= rowCount; row++)
            {
                headcountList.Add(new Headcount
                {
                    PersonnelNumber = GetIntValue(worksheet.Cells[row, 1]),
                    LastName = GetStringValue(worksheet.Cells[row, 2]),
                    FirstName = GetStringValue(worksheet.Cells[row, 3]),
                    AgeOfEmployee = GetIntValue(worksheet.Cells[row, 4]),
                    GenderKey = GetStringValue(worksheet.Cells[row, 5]),
                    Country = GetStringValue(worksheet.Cells[row, 6]),
                    PersonnelSubarea = GetStringValue(worksheet.Cells[row, 7]),
                    Lv = GetStringValue(worksheet.Cells[row, 8]),
                    PersonnelArea = GetStringValue(worksheet.Cells[row, 9]),
                    EmployeeSubgroup = GetStringValue(worksheet.Cells[row, 10]),
                    NameOfSuperior = GetStringValue(worksheet.Cells[row, 11]),
                    Date = GetDateTimeValue(worksheet.Cells[row, 12]),
                    OrganizationalKey = GetStringValue(worksheet.Cells[row, 13]),
                    OrganizationalUnit = GetStringValue(worksheet.Cells[row, 14]),
                    EmployeeGroup = GetStringValue(worksheet.Cells[row, 15]),
                    WeeklyHours = GetStringValue(worksheet.Cells[row, 16]),
                    EmploymentPercentage = GetStringValue(worksheet.Cells[row, 17]),
                    PositionNumber = GetStringValue(worksheet.Cells[row, 18]),
                    PositionTitle = GetStringValue(worksheet.Cells[row, 19]),
                    CostCentreNumber = GetStringValue(worksheet.Cells[row, 20]),
                    CostCentreDescription = GetStringValue(worksheet.Cells[row, 21]),
                    SalariedOrWaged = GetStringValue(worksheet.Cells[row, 22]),
                    Location = GetStringValue(worksheet.Cells[row, 23]),
                    Status = GetStringValue(worksheet.Cells[row, 24]),
                    Tenure = GetStringValue(worksheet.Cells[row, 25]),
                    Month = GetStringValue(worksheet.Cells[row, 26]),
                    BusinessUnit = GetStringValue(worksheet.Cells[row, 27])
                });
            }

            return headcountList;
        }

        private string? GetStringValue(ExcelRange cell) => cell.Value?.ToString()?.Trim();
        private int GetIntValue(ExcelRange cell) => int.TryParse(cell.Value?.ToString(), out int r) ? r : 0;
        private DateTime GetDateTimeValue(ExcelRange cell) =>
            cell.Value is DateTime dt ? dt :
            DateTime.TryParse(cell.Value?.ToString(), out var parsed) ? parsed : DateTime.MinValue;
    }
}
