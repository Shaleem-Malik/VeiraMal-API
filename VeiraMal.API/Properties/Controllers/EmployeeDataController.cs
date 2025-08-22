using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeiraMal.API.Models;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeDataController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EmployeeDataController(AppDbContext context)
        {
            _context = context;
        }

        //terms endpoints
        [HttpPost("terms/uploadd")]
        public async Task<IActionResult> UploadTermsExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var allowedExtensions = new[] { ".xlsx", ".xltx" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                return BadRequest("Invalid file type. Only .xlsx or .xltx allowed.");

            var termsList = new List<Terms>();

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0]; // First sheet
                    var rowCount = worksheet.Dimension.Rows;

                    for (int row = 2; row <= rowCount; row++) // Skip header
                    {
                        if (string.IsNullOrWhiteSpace(worksheet.Cells[row, 1].Text))
                            continue; // Skip empty rows

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
                }
            }

            if (termsList.Count == 0)
                return BadRequest("No valid Terms data found in the file.");

            await _context.Terms.AddRangeAsync(termsList);
            await _context.SaveChangesAsync();

            return Ok($"{termsList.Count} Terms records successfully uploaded.");
        }


        [HttpGet("termss")]
        public async Task<ActionResult<IEnumerable<Terms>>> GetTermsData()
        {
            return await _context.Terms.ToListAsync();
        }

        [HttpGet("terms/analysiss")]
        public async Task<IActionResult> GetTurnoverAnalysis()
        {
            try
            {
                // Get all terms data
                var allTerms = await _context.Terms
                    .AsNoTracking()
                    .ToListAsync();

                // Get headcount data for calculating rates
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

                // Group terms by department
                var turnoverAnalysis = allTerms
                    .GroupBy(t => t.OrganizationalKey ?? "Unknown")
                    .Select(g =>
                    {
                        var dept = g.Key;
                        var deptHeadcount = headcounts.FirstOrDefault(h => h.Department == dept) ??
                            new { Department = dept, TotalCount = 0, MaleCount = 0, FemaleCount = 0 };

                        // Voluntary turnover (resignations, retirements)
                        var voluntaryTerms = g.Where(t => t.Action?.Equals("Voluntary", StringComparison.OrdinalIgnoreCase) == true ||
                                                        t.ReasonForAction?.Contains("Resignation", StringComparison.OrdinalIgnoreCase) == true ||
                                                        t.ReasonForAction?.Contains("Retirement", StringComparison.OrdinalIgnoreCase) == true);

                        var voluntaryMale = voluntaryTerms.Count(t => t.GenderKey == "Male");
                        var voluntaryFemale = voluntaryTerms.Count(t => t.GenderKey == "Female");
                        var voluntaryTotal = voluntaryTerms.Count();

                        // Involuntary turnover (terminations, retrenchments)
                        var involuntaryTerms = g.Where(t => t.Action?.Equals("Involuntary", StringComparison.OrdinalIgnoreCase) == true ||
                                                          t.ReasonForAction?.Contains("Termination", StringComparison.OrdinalIgnoreCase) == true ||
                                                          t.ReasonForAction?.Contains("Retrenchment", StringComparison.OrdinalIgnoreCase) == true);

                        var involuntaryMale = involuntaryTerms.Count(t => t.GenderKey == "Male");
                        var involuntaryFemale = involuntaryTerms.Count(t => t.GenderKey == "Female");
                        var involuntaryTotal = involuntaryTerms.Count();

                        // Calculate rates (protected against division by zero)
                        double SafeRate(int numerator, int denominator) =>
                            denominator > 0 ? Math.Round(numerator * 100.0 / denominator, 2) : 0;

                        return new
                        {
                            Department = dept,
                            // Voluntary turnover
                            VoluntaryTotalRate = SafeRate(voluntaryTotal, deptHeadcount.TotalCount),
                            VoluntaryTotalCount = voluntaryTotal,
                            VoluntaryMaleRate = SafeRate(voluntaryMale, deptHeadcount.MaleCount),
                            VoluntaryMaleCount = voluntaryMale,
                            VoluntaryFemaleRate = SafeRate(voluntaryFemale, deptHeadcount.FemaleCount),
                            VoluntaryFemaleCount = voluntaryFemale,
                            // Involuntary turnover
                            InvoluntaryTotalRate = SafeRate(involuntaryTotal, deptHeadcount.TotalCount),
                            InvoluntaryTotalCount = involuntaryTotal,
                            InvoluntaryMaleRate = SafeRate(involuntaryMale, deptHeadcount.MaleCount),
                            InvoluntaryMaleCount = involuntaryMale,
                            InvoluntaryFemaleRate = SafeRate(involuntaryFemale, deptHeadcount.FemaleCount),
                            InvoluntaryFemaleCount = involuntaryFemale
                        };
                    })
                    .OrderBy(d => d.Department)
                    .ToList();

                return Ok(turnoverAnalysis);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred while calculating turnover analysis: {ex.Message}");
            }
        }


    }

}