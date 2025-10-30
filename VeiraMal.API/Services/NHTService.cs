using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class NHTService : INHTService
    {
        private readonly AppDbContext _context;

        public NHTService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> UploadAsync(IFormFile file)
        {
            var nhtList = await ParseExcelAsync(file);

            if (nhtList.Count == 0)
                throw new InvalidDataException("No valid NHT data found in the file.");

            // Remove old data before adding new
            _context.NHTs.RemoveRange(_context.NHTs);
            await _context.SaveChangesAsync();

            // Add the new records
            await _context.NHTs.AddRangeAsync(nhtList);
            await _context.SaveChangesAsync();

            return $"{nhtList.Count} NHT records successfully uploaded (old data replaced).";
        }

        public async Task<IEnumerable<NHT>> GetAllAsync()
        {
            return await _context.NHTs.AsNoTracking().ToListAsync();
        }

        public async Task<IEnumerable<object>> GetAnalysisAsync()
        {
            var all = await _context.NHTs.AsNoTracking().ToListAsync();

            if (!all.Any())
                return Enumerable.Empty<object>();

            return all
                .GroupBy(n => n.OrganizationalKey ?? "Unknown")
                .Select(g =>
                {
                    var newHires = g.Where(x => x.ActionType != null &&
                        (x.ActionType.Equals("New Hire", StringComparison.OrdinalIgnoreCase) ||
                         x.ActionType.Equals("Hire Employee", StringComparison.OrdinalIgnoreCase)));

                    var transfers = g.Where(x => x.ActionType != null &&
                        (x.ActionType.Contains("Promotion", StringComparison.OrdinalIgnoreCase) ||
                         x.ActionType.Equals("Lateral Move", StringComparison.OrdinalIgnoreCase)));

                    int totalVacantRoles = newHires.Count() + transfers.Count();

                    return new
                    {
                        Department = g.Key,
                        NewHireTotal = newHires.Count(),
                        NewHireMale = newHires.Count(x => x.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true),
                        NewHireFemale = newHires.Count(x => x.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true),
                        TransferTotal = transfers.Count(),
                        TransferMale = transfers.Count(x => x.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true),
                        TransferFemale = transfers.Count(x => x.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true),
                        InternalHireRate = totalVacantRoles > 0
                            ? Math.Round((double)transfers.Count() / totalVacantRoles * 100, 2)
                            : 0
                    };
                })
                .OrderBy(r => r.Department)
                .ToList();
        }

        public async Task<IEnumerable<object>> GetFinanceAnalysisAsync(string month)
        {
            // Only Finance records for the given month
            var financeNHTs = await _context.NHTs
                .AsNoTracking()
                .Where(n => n.OrganizationalKey == "Finance" && n.Month == month)
                .ToListAsync();

            if (!financeNHTs.Any())
                return Enumerable.Empty<object>();

            return financeNHTs
                .GroupBy(n => n.OrganizationalUnit ?? "Unknown") // Group by Unit inside Finance
                .Select(g =>
                {
                    var newHires = g.Where(x => x.ActionType != null &&
                        (x.ActionType.Equals("New Hire", StringComparison.OrdinalIgnoreCase) ||
                         x.ActionType.Equals("Hire Employee", StringComparison.OrdinalIgnoreCase)));

                    var transfers = g.Where(x => x.ActionType != null &&
                        (x.ActionType.Contains("Promotion", StringComparison.OrdinalIgnoreCase) ||
                         x.ActionType.Equals("Lateral Move", StringComparison.OrdinalIgnoreCase)));

                    int totalVacantRoles = newHires.Count() + transfers.Count();

                    return new
                    {
                        OrganizationalUnit = g.Key,
                        NewHireTotal = newHires.Count(),
                        NewHireMale = newHires.Count(x => x.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true),
                        NewHireFemale = newHires.Count(x => x.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true),
                        TransferTotal = transfers.Count(),
                        TransferMale = transfers.Count(x => x.GenderKey?.Equals("Male", StringComparison.OrdinalIgnoreCase) == true),
                        TransferFemale = transfers.Count(x => x.GenderKey?.Equals("Female", StringComparison.OrdinalIgnoreCase) == true),
                        InternalHireRate = totalVacantRoles > 0
                            ? Math.Round((double)transfers.Count() / totalVacantRoles * 100, 2)
                            : 0
                    };
                })
                .OrderBy(r => r.OrganizationalUnit)
                .ToList();
        }



        private async Task<List<NHT>> ParseExcelAsync(IFormFile file)
        {
            var allowedExtensions = new[] { ".xlsx", ".xltx" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                throw new InvalidDataException("Invalid file type. Only .xlsx or .xltx allowed.");

            var nhtList = new List<NHT>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;

                    for (int row = 2; row <= rowCount; row++)
                    {
                        if (string.IsNullOrWhiteSpace(worksheet.Cells[row, 1].Text))
                            continue;

                        nhtList.Add(new NHT
                        {
                            PersonnelNumber = int.TryParse(worksheet.Cells[row, 1].Text, out var pn) ? pn : 0,
                            LastName = worksheet.Cells[row, 2].Text,
                            FirstName = worksheet.Cells[row, 3].Text,
                            AgeOfEmployee = int.TryParse(worksheet.Cells[row, 4].Text, out var age) ? age : 0,
                            GenderKey = worksheet.Cells[row, 5].Text,
                            NameOfSuperior = worksheet.Cells[row, 6].Text,
                            OrganizationalKey = worksheet.Cells[row, 7].Text,
                            OrganizationalUnit = worksheet.Cells[row, 8].Text,
                            PersonnelArea = worksheet.Cells[row, 9].Text,
                            PersonnelSubarea = worksheet.Cells[row, 10].Text,
                            EmployeeGroup = worksheet.Cells[row, 11].Text,
                            EmployeeSubgroup = worksheet.Cells[row, 12].Text,
                            Lv = worksheet.Cells[row, 13].Text,
                            Date = DateTime.TryParse(worksheet.Cells[row, 14].Text, out var date) ? date : DateTime.MinValue,
                            EmploymentPercentage = worksheet.Cells[row, 15].Text,
                            PositionNumber = worksheet.Cells[row, 16].Text,
                            PositionTitle = worksheet.Cells[row, 17].Text,
                            ActionType = worksheet.Cells[row, 18].Text,
                            CostCentreNumber = worksheet.Cells[row, 19].Text,
                            CostCentreDescription = worksheet.Cells[row, 20].Text,
                            SalariedOrWaged = worksheet.Cells[row, 21].Text,
                            Location = worksheet.Cells[row, 22].Text,
                            BusinessUnit = worksheet.Cells[row, 23].Text,
                            Month = worksheet.Cells[row, 24].Text
                        });
                    }
                }
            }

            return nhtList;
        }
    }
}
