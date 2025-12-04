using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class TermsService : ITermsService
    {
        private readonly AppDbContext _context;

        public TermsService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<int> UploadTermsExcelAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("No file uploaded.");

            var allowedExtensions = new[] { ".xlsx", ".xltx" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                throw new ArgumentException("Invalid file type. Only .xlsx or .xltx allowed.");

            var termsList = new List<Terms>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets[0];
            var rowCount = worksheet.Dimension.Rows;

            for (int row = 2; row <= rowCount; row++)
            {
                if (string.IsNullOrWhiteSpace(worksheet.Cells[row, 1].Text))
                    continue;

                termsList.Add(new Terms
                {
                    PersonnelNumber = int.TryParse(worksheet.Cells[row, 1].Text, out var pn) ? pn : 0,
                    LastName = worksheet.Cells[row, 2].Text,
                    FirstName = worksheet.Cells[row, 3].Text,
                    AgeOfEmployee = int.TryParse(worksheet.Cells[row, 4].Text, out var age) ? age : 0,
                    GenderKey = worksheet.Cells[row, 5].Text,
                    OrganizationalKey = worksheet.Cells[row, 6].Text,
                    OrganizationalUnit = worksheet.Cells[row, 7].Text,
                    PersonnelArea = worksheet.Cells[row, 8].Text,
                    PersonnelSubarea = worksheet.Cells[row, 9].Text,
                    EmployeeGroup = worksheet.Cells[row, 10].Text,
                    EmployeeSubgroup = worksheet.Cells[row, 11].Text,
                    Lv = worksheet.Cells[row, 12].Text,
                    Date = DateTime.TryParse(worksheet.Cells[row, 13].Text, out var date) ? date : DateTime.MinValue,
                    EmploymentPercentage = worksheet.Cells[row, 14].Text,
                    ActionType = worksheet.Cells[row, 15].Text,
                    StartDateAction = DateTime.TryParse(worksheet.Cells[row, 16].Text, out var startDate) ? startDate : DateTime.MinValue,
                    ReasonForAction = worksheet.Cells[row, 17].Text,
                    CostCentreNumber = worksheet.Cells[row, 18].Text,
                    CostCentreDescription = worksheet.Cells[row, 19].Text,
                    SalariedOrWaged = worksheet.Cells[row, 20].Text,
                    Manager = worksheet.Cells[row, 21].Text,
                    Action = worksheet.Cells[row, 22].Text,
                    Location = worksheet.Cells[row, 23].Text,
                    BusinessUnit = worksheet.Cells[row, 24].Text,
                    GradeGrouping = worksheet.Cells[row, 25].Text,
                    Month = worksheet.Cells[row, 26].Text
                });
            }

            if (termsList.Count == 0)
                throw new ArgumentException("No valid Terms data found in the file.");

            // Delete old data before inserting new
            _context.Terms.RemoveRange(_context.Terms);
            await _context.SaveChangesAsync();

            await _context.Terms.AddRangeAsync(termsList);
            await _context.SaveChangesAsync();

            return termsList.Count;
        }


        public async Task<IEnumerable<Terms>> GetAllTermsAsync()
        {
            return await _context.Terms.ToListAsync();
        }

        public async Task<object> GetTurnoverAnalysisAsync()
        {
            var allTerms = await _context.Terms.AsNoTracking().ToListAsync();

            var headcounts = await _context.Headcounts
                .AsNoTracking()
                .GroupBy(h => h.OrganizationalKey)
                .Select(g => new
                {
                    Department = g.Key ?? "Unknown",
                    TotalCount = g.Count(),
                    MaleCount = g.Count(h => h.GenderKey == "Male"),
                    FemaleCount = g.Count(h => h.GenderKey == "Female")
                })
                .ToListAsync();

            var turnoverAnalysis = allTerms
                .GroupBy(t => t.OrganizationalKey ?? "Unknown")
                .Select(g =>
                {
                    var dept = g.Key;
                    var deptHeadcount = headcounts.FirstOrDefault(h => h.Department == dept)
                        ?? new { Department = dept, TotalCount = 0, MaleCount = 0, FemaleCount = 0 };

                    var voluntaryTerms = g.Where(t =>
                        t.Action?.Equals("Voluntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Resignation", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retirement", StringComparison.OrdinalIgnoreCase) == true);

                    var involuntaryTerms = g.Where(t =>
                        t.Action?.Equals("Involuntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Termination", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retrenchment", StringComparison.OrdinalIgnoreCase) == true);

                    double SafeRate(int num, int den) => den > 0 ? Math.Round(num * 100.0 / den, 2) : 0;

                    return new
                    {
                        Department = dept,
                        VoluntaryTotalRate = SafeRate(voluntaryTerms.Count(), deptHeadcount.TotalCount),
                        VoluntaryTotalCount = voluntaryTerms.Count(),
                        VoluntaryMaleRate = SafeRate(voluntaryTerms.Count(t => t.GenderKey == "Male"), deptHeadcount.MaleCount),
                        VoluntaryMaleCount = voluntaryTerms.Count(t => t.GenderKey == "Male"),
                        VoluntaryFemaleRate = SafeRate(voluntaryTerms.Count(t => t.GenderKey == "Female"), deptHeadcount.FemaleCount),
                        VoluntaryFemaleCount = voluntaryTerms.Count(t => t.GenderKey == "Female"),
                        InvoluntaryTotalRate = SafeRate(involuntaryTerms.Count(), deptHeadcount.TotalCount),
                        InvoluntaryTotalCount = involuntaryTerms.Count(),
                        InvoluntaryMaleRate = SafeRate(involuntaryTerms.Count(t => t.GenderKey == "Male"), deptHeadcount.MaleCount),
                        InvoluntaryMaleCount = involuntaryTerms.Count(t => t.GenderKey == "Male"),
                        InvoluntaryFemaleRate = SafeRate(involuntaryTerms.Count(t => t.GenderKey == "Female"), deptHeadcount.FemaleCount),
                        InvoluntaryFemaleCount = involuntaryTerms.Count(t => t.GenderKey == "Female")
                    };
                })
                .OrderBy(d => d.Department)
                .ToList();

            return turnoverAnalysis;
        }

        public async Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month)
        {
            // Only Finance records for the given month
            var financeTerms = await _context.Terms
                .AsNoTracking()
                .Where(t => t.OrganizationalKey == "Finance" && t.Month == month)
                .ToListAsync();

            if (!financeTerms.Any())
                return Enumerable.Empty<object>();

            // Also load Finance headcount for denominator calculations
            var financeHeadcounts = await _context.Headcounts
                .AsNoTracking()
                .Where(h => h.OrganizationalKey == "Finance" && h.Month == month)
                .GroupBy(h => h.OrganizationalUnit)
                .Select(g => new
                {
                    Unit = g.Key ?? "Unknown",
                    TotalCount = g.Count(),
                    MaleCount = g.Count(h => h.GenderKey == "Male"),
                    FemaleCount = g.Count(h => h.GenderKey == "Female")
                })
                .ToListAsync();

            return financeTerms
                .GroupBy(t => t.OrganizationalUnit ?? "Unknown") // group by unit inside Finance
                .Select(g =>
                {
                    var unit = g.Key;
                    var unitHeadcount = financeHeadcounts.FirstOrDefault(h => h.Unit == unit)
                        ?? new { Unit = unit, TotalCount = 0, MaleCount = 0, FemaleCount = 0 };

                    var voluntary = g.Where(t =>
                        t.Action?.Equals("Voluntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Resignation", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retirement", StringComparison.OrdinalIgnoreCase) == true);

                    var involuntary = g.Where(t =>
                        t.Action?.Equals("Involuntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Termination", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retrenchment", StringComparison.OrdinalIgnoreCase) == true);

                    double SafeRate(int num, int den) => den > 0 ? Math.Round(num * 100.0 / den, 2) : 0;

                    return new
                    {
                        OrganizationalUnit = unit,
                        VoluntaryTotalRate = SafeRate(voluntary.Count(), unitHeadcount.TotalCount),
                        VoluntaryTotalCount = voluntary.Count(),
                        VoluntaryMaleRate = SafeRate(voluntary.Count(t => t.GenderKey == "Male"), unitHeadcount.MaleCount),
                        VoluntaryMaleCount = voluntary.Count(t => t.GenderKey == "Male"),
                        VoluntaryFemaleRate = SafeRate(voluntary.Count(t => t.GenderKey == "Female"), unitHeadcount.FemaleCount),
                        VoluntaryFemaleCount = voluntary.Count(t => t.GenderKey == "Female"),
                        InvoluntaryTotalRate = SafeRate(involuntary.Count(), unitHeadcount.TotalCount),
                        InvoluntaryTotalCount = involuntary.Count(),
                        InvoluntaryMaleRate = SafeRate(involuntary.Count(t => t.GenderKey == "Male"), unitHeadcount.MaleCount),
                        InvoluntaryMaleCount = involuntary.Count(t => t.GenderKey == "Male"),
                        InvoluntaryFemaleRate = SafeRate(involuntary.Count(t => t.GenderKey == "Female"), unitHeadcount.FemaleCount),
                        InvoluntaryFemaleCount = involuntary.Count(t => t.GenderKey == "Female")
                    };
                })
                .OrderBy(r => r.OrganizationalUnit)
                .ToList();
        }

        public async Task<byte[]> ExportTurnoverAnalysisAsync()
        {
            // reuse your analysis logic to build rows
            var allTerms = await _context.Terms.AsNoTracking().ToListAsync();

            var headcounts = await _context.Headcounts
                .AsNoTracking()
                .GroupBy(h => h.OrganizationalKey)
                .Select(g => new
                {
                    Department = g.Key ?? "Unknown",
                    TotalCount = g.Count(),
                    MaleCount = g.Count(h => h.GenderKey == "Male"),
                    FemaleCount = g.Count(h => h.GenderKey == "Female")
                })
                .ToListAsync();

            var rows = allTerms
                .GroupBy(t => t.OrganizationalKey ?? "Unknown")
                .Select(g =>
                {
                    var dept = g.Key;
                    var deptHeadcount = headcounts.FirstOrDefault(h => h.Department == dept)
                        ?? new { Department = dept, TotalCount = 0, MaleCount = 0, FemaleCount = 0 };

                    var voluntaryTerms = g.Where(t =>
                        t.Action?.Equals("Voluntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Resignation", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retirement", StringComparison.OrdinalIgnoreCase) == true);

                    var involuntaryTerms = g.Where(t =>
                        t.Action?.Equals("Involuntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Termination", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retrenchment", StringComparison.OrdinalIgnoreCase) == true);

                    double SafeRate(int num, int den) => den > 0 ? Math.Round(num * 100.0 / den, 2) : 0;

                    return new
                    {
                        Department = dept,
                        VoluntaryTotalRate = SafeRate(voluntaryTerms.Count(), deptHeadcount.TotalCount),
                        VoluntaryTotalCount = voluntaryTerms.Count(),
                        VoluntaryMaleRate = SafeRate(voluntaryTerms.Count(t => t.GenderKey == "Male"), deptHeadcount.MaleCount),
                        VoluntaryMaleCount = voluntaryTerms.Count(t => t.GenderKey == "Male"),
                        VoluntaryFemaleRate = SafeRate(voluntaryTerms.Count(t => t.GenderKey == "Female"), deptHeadcount.FemaleCount),
                        VoluntaryFemaleCount = voluntaryTerms.Count(t => t.GenderKey == "Female"),
                        InvoluntaryTotalRate = SafeRate(involuntaryTerms.Count(), deptHeadcount.TotalCount),
                        InvoluntaryTotalCount = involuntaryTerms.Count(),
                        InvoluntaryMaleRate = SafeRate(involuntaryTerms.Count(t => t.GenderKey == "Male"), deptHeadcount.MaleCount),
                        InvoluntaryMaleCount = involuntaryTerms.Count(t => t.GenderKey == "Male"),
                        InvoluntaryFemaleRate = SafeRate(involuntaryTerms.Count(t => t.GenderKey == "Female"), deptHeadcount.FemaleCount),
                        InvoluntaryFemaleCount = involuntaryTerms.Count(t => t.GenderKey == "Female")
                    };
                })
                .OrderBy(d => d.Department)
                .ToList();

            if (!rows.Any())
                return Array.Empty<byte>();

            //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Turnover Analysis");
                var headers = new[]
                {
                "Department",
                "VoluntaryTotalRate",
                "VoluntaryTotalCount",
                "VoluntaryMaleRate",
                "VoluntaryMaleCount",
                "VoluntaryFemaleRate",
                "VoluntaryFemaleCount",
                "InvoluntaryTotalRate",
                "InvoluntaryTotalCount",
                "InvoluntaryMaleRate",
                "InvoluntaryMaleCount",
                "InvoluntaryFemaleRate",
                "InvoluntaryFemaleCount"
            };

                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cells[1, i + 1].Value = headers[i];
                    ws.Cells[1, i + 1].Style.Font.Bold = true;
                }

                int rowIndex = 2;
                foreach (var r in rows)
                {
                    ws.Cells[rowIndex, 1].Value = r.Department;
                    ws.Cells[rowIndex, 2].Value = r.VoluntaryTotalRate;
                    ws.Cells[rowIndex, 3].Value = r.VoluntaryTotalCount;
                    ws.Cells[rowIndex, 4].Value = r.VoluntaryMaleRate;
                    ws.Cells[rowIndex, 5].Value = r.VoluntaryMaleCount;
                    ws.Cells[rowIndex, 6].Value = r.VoluntaryFemaleRate;
                    ws.Cells[rowIndex, 7].Value = r.VoluntaryFemaleCount;
                    ws.Cells[rowIndex, 8].Value = r.InvoluntaryTotalRate;
                    ws.Cells[rowIndex, 9].Value = r.InvoluntaryTotalCount;
                    ws.Cells[rowIndex, 10].Value = r.InvoluntaryMaleRate;
                    ws.Cells[rowIndex, 11].Value = r.InvoluntaryMaleCount;
                    ws.Cells[rowIndex, 12].Value = r.InvoluntaryFemaleRate;
                    ws.Cells[rowIndex, 13].Value = r.InvoluntaryFemaleCount;

                    // format rates
                    ws.Cells[rowIndex, 2].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 4].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 6].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 8].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 10].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 12].Style.Numberformat.Format = "0.00";

                    rowIndex++;
                }

                ws.Cells[1, 1, rowIndex - 1, headers.Length].AutoFitColumns();
                return package.GetAsByteArray();
            }
        }

        public async Task<byte[]> ExportFinanceAnalysisAsync(string month)
        {
            var monthNorm = (month ?? "").Trim();
            if (string.IsNullOrEmpty(monthNorm))
                monthNorm = DateTime.UtcNow.ToString("MMMM");

            var financeTerms = await _context.Terms
                .AsNoTracking()
                .Where(t => t.OrganizationalKey == "Finance" && t.Month == monthNorm)
                .ToListAsync();

            if (!financeTerms.Any())
                return Array.Empty<byte>();

            var financeHeadcounts = await _context.Headcounts
                .AsNoTracking()
                .Where(h => h.OrganizationalKey == "Finance" && h.Month == monthNorm)
                .GroupBy(h => h.OrganizationalUnit)
                .Select(g => new
                {
                    Unit = g.Key ?? "Unknown",
                    TotalCount = g.Count(),
                    MaleCount = g.Count(h => h.GenderKey == "Male"),
                    FemaleCount = g.Count(h => h.GenderKey == "Female")
                })
                .ToListAsync();

            var rows = financeTerms
                .GroupBy(t => t.OrganizationalUnit ?? "Unknown")
                .Select(g =>
                {
                    var unit = g.Key;
                    var unitHeadcount = financeHeadcounts.FirstOrDefault(h => h.Unit == unit)
                        ?? new { Unit = unit, TotalCount = 0, MaleCount = 0, FemaleCount = 0 };

                    var voluntary = g.Where(t =>
                        t.Action?.Equals("Voluntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Resignation", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retirement", StringComparison.OrdinalIgnoreCase) == true);

                    var involuntary = g.Where(t =>
                        t.Action?.Equals("Involuntary", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Termination", StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReasonForAction?.Contains("Retrenchment", StringComparison.OrdinalIgnoreCase) == true);

                    double SafeRate(int num, int den) => den > 0 ? Math.Round(num * 100.0 / den, 2) : 0;

                    return new
                    {
                        OrganizationalUnit = unit,
                        VoluntaryTotalRate = SafeRate(voluntary.Count(), unitHeadcount.TotalCount),
                        VoluntaryTotalCount = voluntary.Count(),
                        VoluntaryMaleRate = SafeRate(voluntary.Count(t => t.GenderKey == "Male"), unitHeadcount.MaleCount),
                        VoluntaryMaleCount = voluntary.Count(t => t.GenderKey == "Male"),
                        VoluntaryFemaleRate = SafeRate(voluntary.Count(t => t.GenderKey == "Female"), unitHeadcount.FemaleCount),
                        VoluntaryFemaleCount = voluntary.Count(t => t.GenderKey == "Female"),
                        InvoluntaryTotalRate = SafeRate(involuntary.Count(), unitHeadcount.TotalCount),
                        InvoluntaryTotalCount = involuntary.Count(),
                        InvoluntaryMaleRate = SafeRate(involuntary.Count(t => t.GenderKey == "Male"), unitHeadcount.MaleCount),
                        InvoluntaryMaleCount = involuntary.Count(t => t.GenderKey == "Male"),
                        InvoluntaryFemaleRate = SafeRate(involuntary.Count(t => t.GenderKey == "Female"), unitHeadcount.FemaleCount),
                        InvoluntaryFemaleCount = involuntary.Count(t => t.GenderKey == "Female")
                    };
                })
                .OrderBy(r => r.OrganizationalUnit)
                .ToList();

            //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add($"Terms_Finance_{monthNorm}");
                var headers = new[]
                {
                "OrganizationalUnit",
                "VoluntaryTotalRate",
                "VoluntaryTotalCount",
                "VoluntaryMaleRate",
                "VoluntaryMaleCount",
                "VoluntaryFemaleRate",
                "VoluntaryFemaleCount",
                "InvoluntaryTotalRate",
                "InvoluntaryTotalCount",
                "InvoluntaryMaleRate",
                "InvoluntaryMaleCount",
                "InvoluntaryFemaleRate",
                "InvoluntaryFemaleCount"
            };

                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cells[1, i + 1].Value = headers[i];
                    ws.Cells[1, i + 1].Style.Font.Bold = true;
                }

                int rowIndex = 2;
                foreach (var r in rows)
                {
                    ws.Cells[rowIndex, 1].Value = r.OrganizationalUnit;
                    ws.Cells[rowIndex, 2].Value = r.VoluntaryTotalRate;
                    ws.Cells[rowIndex, 3].Value = r.VoluntaryTotalCount;
                    ws.Cells[rowIndex, 4].Value = r.VoluntaryMaleRate;
                    ws.Cells[rowIndex, 5].Value = r.VoluntaryMaleCount;
                    ws.Cells[rowIndex, 6].Value = r.VoluntaryFemaleRate;
                    ws.Cells[rowIndex, 7].Value = r.VoluntaryFemaleCount;
                    ws.Cells[rowIndex, 8].Value = r.InvoluntaryTotalRate;
                    ws.Cells[rowIndex, 9].Value = r.InvoluntaryTotalCount;
                    ws.Cells[rowIndex, 10].Value = r.InvoluntaryMaleRate;
                    ws.Cells[rowIndex, 11].Value = r.InvoluntaryMaleCount;
                    ws.Cells[rowIndex, 12].Value = r.InvoluntaryFemaleRate;
                    ws.Cells[rowIndex, 13].Value = r.InvoluntaryFemaleCount;

                    ws.Cells[rowIndex, 2].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 4].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 6].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 8].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 10].Style.Numberformat.Format = "0.00";
                    ws.Cells[rowIndex, 12].Style.Numberformat.Format = "0.00";

                    rowIndex++;
                }

                ws.Cells[1, 1, rowIndex - 1, headers.Length].AutoFitColumns();
                return package.GetAsByteArray();
            }
        }

    }
}
