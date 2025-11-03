using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models;
using VeiraMal.API.ViewModels.Analytics;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AnalyticsController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Gender by Department
        [HttpGet("gender-by-department")]
        public ActionResult<IEnumerable<GenderByDepartmentViewModel>> GetGenderByDepartment()
        {
            var data = _context.Employees
                .Where(e => !string.IsNullOrEmpty(e.Department) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.Department, e.Gender })
                .Select(g => new GenderByDepartmentViewModel
                {
                    Department = g.Key.Department,
                    Gender = g.Key.Gender,
                    Count = g.Count()
                })
                .ToList();

            return Ok(data);
        }

        // 2. Gender by Location
        [HttpGet("gender-by-location")]
        public ActionResult<IEnumerable<GenderByLocationViewModel>> GetGenderByLocation()
        {
            var data = _context.Employees
                .Where(e => !string.IsNullOrEmpty(e.Location) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.Location, e.Gender })
                .Select(g => new GenderByLocationViewModel
                {
                    Location = g.Key.Location,
                    Gender = g.Key.Gender,
                    Count = g.Count()
                })
                .ToList();

            return Ok(data);
        }

        // 3. Average Tenure
        [HttpGet("average-tenure")]
        public ActionResult<AverageTenureViewModel> GetAverageTenure()
        {
            var today = DateTime.UtcNow;
            var averageYears = _context.Employees
                .Select(e => EF.Functions.DateDiffDay(e.HireDate, today) / 365.0)
                .Average();

            return Ok(new AverageTenureViewModel
            {
                AverageTenureInYears = Math.Round(averageYears, 2)
            });
        }

        // 4. Position vs Salary Gap (Male vs Female)
        [HttpGet("position-salary-gap")]
        public ActionResult<IEnumerable<PositionSalaryGapViewModel>> GetPositionSalaryGap()
        {
            var data = _context.Employees
                .Where(e => !string.IsNullOrEmpty(e.PositionTitle) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.PositionTitle, e.Gender })
                .Select(g => new
                {
                    g.Key.PositionTitle,
                    g.Key.Gender,
                    AvgSalary = g.Average(e => e.BaseSalary)
                })
                .ToList()
                .GroupBy(x => x.PositionTitle)
                .Select(g =>
                {
                    var male = g.FirstOrDefault(x => x.Gender == "Male")?.AvgSalary ?? 0;
                    var female = g.FirstOrDefault(x => x.Gender == "Female")?.AvgSalary ?? 0;

                    return new PositionSalaryGapViewModel
                    {
                        PositionTitle = g.Key,
                        MaleAverageSalary = male,
                        FemaleAverageSalary = female,
                        SalaryGap = male - female
                    };
                })
                .ToList();

            return Ok(data);
        }

        // 5. Gender by Manager
        [HttpGet("gender-by-manager")]
        public ActionResult<IEnumerable<GenderByManagerViewModel>> GetGenderByManager()
        {
            var data = _context.Employees
                .Where(e => !string.IsNullOrEmpty(e.ManagerEmployeeId) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.ManagerEmployeeId, e.Gender })
                .Select(g => new GenderByManagerViewModel
                {
                    ManagerEmployeeId = g.Key.ManagerEmployeeId,
                    Gender = g.Key.Gender,
                    Count = g.Count()
                })
                .ToList();

            return Ok(data);
        }

        // 6. Manager Salary Gap
        [HttpGet("manager-salary-gap")]
        public ActionResult<IEnumerable<ManagerSalaryGapViewModel>> GetManagerSalaryGap()
        {
            var data = _context.Employees
                .Where(e => !string.IsNullOrEmpty(e.ManagerEmployeeId) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.ManagerEmployeeId, e.Gender })
                .Select(g => new
                {
                    g.Key.ManagerEmployeeId,
                    g.Key.Gender,
                    AvgSalary = g.Average(e => e.BaseSalary)
                })
                .ToList()
                .GroupBy(x => x.ManagerEmployeeId)
                .Select(g =>
                {
                    var male = g.FirstOrDefault(x => x.Gender == "Male")?.AvgSalary ?? 0;
                    var female = g.FirstOrDefault(x => x.Gender == "Female")?.AvgSalary ?? 0;

                    return new ManagerSalaryGapViewModel
                    {
                        ManagerEmployeeId = g.Key,
                        AverageSalaryMale = male,
                        AverageSalaryFemale = female,
                        SalaryGap = male - female
                    };
                })
                .ToList();

            return Ok(data);
        }
    }
}
