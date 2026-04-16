using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LiabilityController : ControllerBase
    {
        private readonly ILiabilityService _liabilityService;
        private readonly AppDbContext _context;

        public LiabilityController(ILiabilityService liabilityService, AppDbContext context)
        {
            _liabilityService = liabilityService;
            _context = context;
        }

        [HttpPost("calculate")]
        public async Task<ActionResult> Calculate()
        {
            var result = await _liabilityService.CalculateLiabilitiesAsync();
            return Ok(new { Message = result });
        }

        [HttpGet("employee/{employeeId:int}")]
        public async Task<ActionResult<EmployeeLiabilityDto>> GetEmployeeLiability(int employeeId, DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            var rec = await _context.EmployeeLiabilities
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.CalculationDate >= calcDateStart && x.CalculationDate < calcDateEnd)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (rec == null) return NotFound();

            var dto = new EmployeeLiabilityDto
            {
                EmployeeId = rec.EmployeeId,
                CalculationDate = rec.CalculationDate,
                BalanceDays = rec.BalanceDays,
                EntitlementDays = rec.EntitlementDays,
                TotalLeaveBalance = rec.TotalLeaveBalance,
                FutureLeaveBookedDays = rec.FutureLeaveBookedDays,
                TargetDays = rec.TargetDays,
                DaysLeftToTake = rec.DaysLeftToTake,
                DailyRate = rec.DailyRate,
                LiabilityAmount = rec.LiabilityAmount,
                BenefitDays = rec.BenefitDays,
                BenefitAmount = rec.BenefitAmount
            };

            return Ok(dto);
        }

        [HttpGet("top-employees")]
        public async Task<ActionResult<IEnumerable<TopEmployeeDto>>> GetTopEmployees(int limit = 50, DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            var topQuery = await _context.EmployeeLiabilities
                .AsNoTracking()
                .Where(x => x.CalculationDate >= calcDateStart && x.CalculationDate < calcDateEnd)
                .GroupBy(x => x.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, TotalLiability = g.Sum(x => x.LiabilityAmount) })
                .OrderByDescending(x => x.TotalLiability)
                .Take(limit)
                .ToListAsync();

            if (!topQuery.Any()) return Ok(new List<TopEmployeeDto>());

            var employeeIds = topQuery.Select(x => x.EmployeeId).Where(id => id != 0).Distinct().ToArray();

            var headcountRows = await _context.Headcounts
                .AsNoTracking()
                .Where(h => employeeIds.Contains(h.PersonnelNumber))
                .ToListAsync();

            var namesMap = headcountRows
                .Where(h => h.PersonnelNumber != 0)
                .GroupBy(h => h.PersonnelNumber)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Id).FirstOrDefault());

            var result = topQuery.Select(x =>
            {
                string? name = null;
                if (x.EmployeeId != 0 && namesMap.TryGetValue(x.EmployeeId, out var hc) && hc != null)
                {
                    name = (hc.FirstName + " " + hc.LastName).Trim();
                }

                return new TopEmployeeDto
                {
                    EmployeeId = x.EmployeeId,
                    EmployeeName = name,
                    TotalLiability = x.TotalLiability
                };
            }).ToList();

            return Ok(result);
        }

        [HttpGet("top-departments")]
        public async Task<ActionResult<IEnumerable<DepartmentSummaryDto>>> GetTopDepartments(int limit = 50, DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            var q = from el in _context.EmployeeLiabilities
                    join hc in _context.Headcounts on el.EmployeeId equals hc.PersonnelNumber
                    where el.CalculationDate >= calcDateStart && el.CalculationDate < calcDateEnd
                    group new { el, hc } by hc.OrganizationalUnit into g
                    select new
                    {
                        Department = g.Key ?? "Unknown",
                        TotalLiability = g.Sum(x => x.el.LiabilityAmount),
                        AvgLeaveBalance = (decimal)g.Average(x => x.el.TotalLeaveBalance),
                        Headcount = g.Select(x => x.hc.PersonnelNumber).Distinct().Count()
                    };

            var top = await q.OrderByDescending(x => x.TotalLiability).Take(limit).ToListAsync();

            var dto = top.Select(x => new DepartmentSummaryDto
            {
                Department = x.Department,
                TotalLiability = x.TotalLiability,
                AverageLeaveBalance = x.AvgLeaveBalance,
                Headcount = x.Headcount
            });

            return Ok(dto);
        }

        [HttpGet("region-summary")]
        public async Task<ActionResult<IEnumerable<RegionSummaryDto>>> GetRegionSummary(DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            var q = from el in _context.EmployeeLiabilities
                    join hc in _context.Headcounts on el.EmployeeId equals hc.PersonnelNumber
                    where el.CalculationDate >= calcDateStart && el.CalculationDate < calcDateEnd
                    group new { el, hc } by hc.BusinessUnit into g
                    select new
                    {
                        Region = g.Key ?? "Unknown",
                        AvgBalance = (decimal)g.Average(x => x.el.TotalLeaveBalance),
                        Headcount = g.Select(x => x.hc.PersonnelNumber).Distinct().Count()
                    };

            var result = await q.ToListAsync();

            var dto = result.Select(x => new RegionSummaryDto
            {
                Region = x.Region,
                AverageLeaveBalance = x.AvgBalance,
                Headcount = x.Headcount
            });

            return Ok(dto);
        }

        [HttpGet("latest-calculation")]
        public async Task<ActionResult<DateTime?>> GetLatestCalculationDate()
        {
            var latest = await _context.EmployeeLiabilities.MaxAsync(x => (DateTime?)x.CalculationDate);
            return Ok(latest);
        }

        [HttpGet("tracker")]
        public async Task<ActionResult<IEnumerable<EmployeeLiabilityFullDto>>> GetLiabilityTracker()
        {
            var liabilities = await _context.EmployeeLiabilities
                .AsNoTracking()
                .ToListAsync();

            if (!liabilities.Any()) return Ok(new List<EmployeeLiabilityFullDto>());

            var employeeIds = liabilities.Select(l => l.EmployeeId).Where(id => id != 0).Distinct().ToArray();

            var headcountRows = await _context.Headcounts
                .AsNoTracking()
                .Where(h => employeeIds.Contains(h.PersonnelNumber))
                .ToListAsync();

            var headcountMap = headcountRows
                .Where(h => h.PersonnelNumber != 0)
                .GroupBy(h => h.PersonnelNumber)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(h => h.Id).FirstOrDefault()
                );

            var leaveBalancesRows = await _context.LeaveBalances
                .AsNoTracking()
                .Where(lb => employeeIds.Contains(lb.EmployeeId))
                .ToListAsync();

            var leaveBalanceMap = leaveBalancesRows
                .Where(lb => lb.EmployeeId != 0)
                .GroupBy(lb => lb.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(lb => lb.Id).FirstOrDefault());

            var result = liabilities.Select(l =>
            {
                headcountMap.TryGetValue(l.EmployeeId, out var hc);
                leaveBalanceMap.TryGetValue(l.EmployeeId, out var lb);

                var name = (hc != null)
                    ? string.Join(" ", new[] { hc.FirstName, hc.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : null;

                return new EmployeeLiabilityFullDto
                {
                    EmployeeId = l.EmployeeId,
                    CalculationDate = l.CalculationDate,
                    BalanceDays = l.BalanceDays,
                    EntitlementDays = l.EntitlementDays,
                    TotalLeaveBalance = l.TotalLeaveBalance,
                    FutureLeaveBookedDays = l.FutureLeaveBookedDays,
                    TargetDays = l.TargetDays,
                    DaysLeftToTake = l.DaysLeftToTake,
                    DailyRate = l.DailyRate,
                    LiabilityAmount = l.LiabilityAmount,
                    BenefitDays = l.BenefitDays,
                    BenefitAmount = l.BenefitAmount,
                    SourceUploadBatchId = l.SourceUploadBatchId,
                    CreatedAt = l.CreatedAt,

                    EmployeeName = name,
                    Position = hc?.PositionTitle,
                    PayCategory = hc?.SalariedOrWaged,
                    OrganizationalUnit = hc?.OrganizationalUnit,
                    Function = hc?.OrganizationalKey,
                    Location = hc?.Location,
                    BusinessUnit = hc?.BusinessUnit,
                    NextAnniversaryDate = lb?.ALNextAnniversary,
                    ManagerName = hc?.NameOfSuperior,
                    WeeklyHours = hc?.WeeklyHours
                };
            })
            .OrderByDescending(x => x.LiabilityAmount)
            .ToList();

            return Ok(result);
        }
    }
}