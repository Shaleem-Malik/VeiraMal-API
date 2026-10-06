using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeiraMal.API.ViewModels.Analytics;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AnalyticsController(AppDbContext context) => _context = context;

        private bool TryGetCompanyId(out Guid companyId) => Guid.TryParse(User.FindFirstValue("companyId"), out companyId);

        [HttpGet("gender-by-department")]
        public async Task<ActionResult<IEnumerable<GenderByDepartmentViewModel>>> GetGenderByDepartment()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var data = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && !string.IsNullOrEmpty(e.Department) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.Department, e.Gender })
                .Select(g => new GenderByDepartmentViewModel { Department = g.Key.Department!, Gender = g.Key.Gender!, Count = g.Count() })
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("gender-by-location")]
        public async Task<ActionResult<IEnumerable<GenderByLocationViewModel>>> GetGenderByLocation()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var data = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && !string.IsNullOrEmpty(e.Location) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.Location, e.Gender })
                .Select(g => new GenderByLocationViewModel { Location = g.Key.Location!, Gender = g.Key.Gender!, Count = g.Count() })
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("average-tenure")]
        public async Task<ActionResult<AverageTenureViewModel>> GetAverageTenure()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var today = DateTime.UtcNow.Date;
            var hireDates = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && e.HireDate <= today)
                .Select(e => e.HireDate)
                .ToListAsync();
            var averageYears = hireDates.Count == 0 ? 0 : hireDates.Average(d => (today - d.Date).TotalDays / 365.25);
            return Ok(new AverageTenureViewModel { AverageTenureInYears = Math.Round(averageYears, 2) });
        }

        [HttpGet("position-salary-gap")]
        public async Task<ActionResult<IEnumerable<PositionSalaryGapViewModel>>> GetPositionSalaryGap()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var data = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && !string.IsNullOrEmpty(e.PositionTitle) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.PositionTitle, e.Gender })
                .Select(g => new { g.Key.PositionTitle, g.Key.Gender, AvgSalary = g.Average(e => e.BaseSalary) })
                .ToListAsync();

            return Ok(data.GroupBy(x => x.PositionTitle).Select(g => new PositionSalaryGapViewModel
            {
                PositionTitle = g.Key!,
                MaleAverageSalary = g.Where(x => x.Gender == "Male").Select(x => x.AvgSalary).FirstOrDefault(),
                FemaleAverageSalary = g.Where(x => x.Gender == "Female").Select(x => x.AvgSalary).FirstOrDefault(),
                SalaryGap = g.Where(x => x.Gender == "Male").Select(x => x.AvgSalary).FirstOrDefault() - g.Where(x => x.Gender == "Female").Select(x => x.AvgSalary).FirstOrDefault()
            }).ToList());
        }

        [HttpGet("gender-by-manager")]
        public async Task<ActionResult<IEnumerable<GenderByManagerViewModel>>> GetGenderByManager()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var data = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && !string.IsNullOrEmpty(e.ManagerEmployeeId) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.ManagerEmployeeId, e.Gender })
                .Select(g => new GenderByManagerViewModel { ManagerEmployeeId = g.Key.ManagerEmployeeId!, Gender = g.Key.Gender!, Count = g.Count() })
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("manager-salary-gap")]
        public async Task<ActionResult<IEnumerable<ManagerSalaryGapViewModel>>> GetManagerSalaryGap()
        {
            if (!TryGetCompanyId(out var companyId)) return Unauthorized();
            var data = await _context.Employees.AsNoTracking()
                .Where(e => e.CompanyId == companyId && !string.IsNullOrEmpty(e.ManagerEmployeeId) && !string.IsNullOrEmpty(e.Gender))
                .GroupBy(e => new { e.ManagerEmployeeId, e.Gender })
                .Select(g => new { g.Key.ManagerEmployeeId, g.Key.Gender, AvgSalary = g.Average(e => e.BaseSalary) })
                .ToListAsync();

            return Ok(data.GroupBy(x => x.ManagerEmployeeId).Select(g => new ManagerSalaryGapViewModel
            {
                ManagerEmployeeId = g.Key!,
                AverageSalaryMale = g.Where(x => x.Gender == "Male").Select(x => x.AvgSalary).FirstOrDefault(),
                AverageSalaryFemale = g.Where(x => x.Gender == "Female").Select(x => x.AvgSalary).FirstOrDefault(),
                SalaryGap = g.Where(x => x.Gender == "Male").Select(x => x.AvgSalary).FirstOrDefault() - g.Where(x => x.Gender == "Female").Select(x => x.AvgSalary).FirstOrDefault()
            }).ToList());
        }
    }
}
