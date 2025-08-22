using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Globalization;
using VeiraMal.API;
using VeiraMal.API.Models;
using Microsoft.AspNetCore.Authorization;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UploadController(AppDbContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpPost("excel")]
        public async Task<IActionResult> UploadExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            try
            {
                // EPPlus License Setup for version 8+
                //ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                using var package = new ExcelPackage(stream);
                var worksheet = package.Workbook.Worksheets[0];

                var rowCount = worksheet.Dimension.Rows;
                var employees = new List<Employee>();

                for (int row = 2; row <= rowCount; row++)
                {
                    var employee = new Employee
                    {
                        EmployeeId = worksheet.Cells[row, 1].Text,
                        Gender = worksheet.Cells[row, 2].Text,

                        BaseSalary = ParseCurrency(worksheet.Cells[row, 3].Text),
                        TotalRemuneration = ParseCurrency(worksheet.Cells[row, 4].Text),
                        SuperPercentage = ParsePercentage(worksheet.Cells[row, 5].Text),

                        BusinessUnit = worksheet.Cells[row, 6].Text,
                        Department = worksheet.Cells[row, 7].Text,
                        OrgUnit = worksheet.Cells[row, 8].Text,
                        Location = worksheet.Cells[row, 9].Text,

                        DateOfBirth = ParseDate(worksheet.Cells[row, 10].Text),
                        HireDate = ParseDate(worksheet.Cells[row, 11].Text),

                        PositionTitle = worksheet.Cells[row, 12].Text,
                        ManagerEmployeeId = worksheet.Cells[row, 13].Text,

                        FTE = ParseDecimal(worksheet.Cells[row, 14].Text),
                        HoursPerWeek = ParseDecimal(worksheet.Cells[row, 15].Text),
                        Level = worksheet.Cells[row, 16].Text,
                    };

                    employees.Add(employee);
                }

                _context.Employees.AddRange(employees);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"{employees.Count} records saved successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error processing file: " + ex.Message);
            }
        }

        private decimal ParseCurrency(string input)
        {
            input = input.Replace(",", "").Replace("$", "").Trim();
            return decimal.TryParse(input, out var result) ? result : 0;
        }

        private decimal ParsePercentage(string input)
        {
            input = input.Replace("%", "").Trim();
            return decimal.TryParse(input, out var result) ? result : 0;
        }

        private DateTime ParseDate(string input)
        {
            return DateTime.TryParse(input, out var date) ? date : DateTime.MinValue;
        }

        private decimal ParseDecimal(string input)
        {
            return decimal.TryParse(input, out var value) ? value : 0;
        }
    }
}
