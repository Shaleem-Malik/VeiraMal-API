using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // Trigger calculation for a given upload batch (recommended)
        [HttpPost("calculate/{batchId:int}")]
        public async Task<ActionResult> Calculate(int batchId)
        {
            // basic validation: check batch exists
            var batch = await _context.UploadBatches.FindAsync(batchId);
            if (batch == null) return NotFound($"Batch {batchId} not found.");

            var result = await _liabilityService.CalculateLiabilitiesAsync(batchId);
            return Ok(new { Message = result });
        }

        // Get per-employee liability (latest calculation or by date)
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

        // Top N employees by liability
        [HttpGet("top-employees")]
        public async Task<ActionResult<IEnumerable<TopEmployeeDto>>> GetTopEmployees(int limit = 50, DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            // 1) get top employees by liability (employee ids and totals)
            var topQuery = await _context.EmployeeLiabilities
                .AsNoTracking()
                .Where(x => x.CalculationDate >= calcDateStart && x.CalculationDate < calcDateEnd)
                .GroupBy(x => x.EmployeeId)
                .Select(g => new { EmployeeId = g.Key, TotalLiability = g.Sum(x => x.LiabilityAmount) })
                .OrderByDescending(x => x.TotalLiability)
                .Take(limit)
                .ToListAsync();

            if (!topQuery.Any()) return Ok(new List<TopEmployeeDto>());

            // 2) get the set of employee ids we need (ignore 0)
            var employeeIds = topQuery.Select(x => x.EmployeeId).Where(id => id != 0).Distinct().ToArray();

            // 3) load Headcounts rows for those employees and create a safe map:
            var headcountRows = await _context.Headcounts
                .AsNoTracking()
                .Where(h => employeeIds.Contains(h.PersonnelNumber))
                .ToListAsync();

            var namesMap = headcountRows
                .Where(h => h.PersonnelNumber != 0)
                .GroupBy(h => h.PersonnelNumber)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Id).FirstOrDefault());

            // 4) build DTOs using the safe map; if no name exists, leave EmployeeName null
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


        // Top N departments by liability
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

        // Average by business unit / region
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

        // Return latest available calculation date (helpful for UI)
        [HttpGet("latest-calculation")]
        public async Task<ActionResult<DateTime?>> GetLatestCalculationDate()
        {
            var latest = await _context.EmployeeLiabilities.MaxAsync(x => (DateTime?)x.CalculationDate);
            return Ok(latest);
        }

        [HttpGet("tracker")]
        public async Task<ActionResult<IEnumerable<EmployeeLiabilityFullDto>>> GetLiabilityTracker(DateTime? date = null)
        {
            DateTime calcDateStart = date?.Date ?? DateTime.UtcNow.Date;
            DateTime calcDateEnd = calcDateStart.AddDays(1);

            // 1) Load liabilities for the requested calculation date (range)
            var liabilities = await _context.EmployeeLiabilities
                .AsNoTracking()
                .Where(x => x.CalculationDate >= calcDateStart && x.CalculationDate < calcDateEnd)
                .ToListAsync();

            if (!liabilities.Any()) return Ok(new List<EmployeeLiabilityFullDto>());

            // 2) Get the distinct employee ids (ignore zero)
            var employeeIds = liabilities.Select(l => l.EmployeeId).Where(id => id != 0).Distinct().ToArray();

            // 3) Prefetch Headcounts for those employees
            var headcountRows = await _context.Headcounts
                .AsNoTracking()
                .Where(h => employeeIds.Contains(h.PersonnelNumber))
                .ToListAsync();

            // Create a map: PersonnelNumber -> most recent Headcount row (by Id)
            var headcountMap = headcountRows
                .Where(h => h.PersonnelNumber != 0)
                .GroupBy(h => h.PersonnelNumber)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(h => h.Id).FirstOrDefault()
                );

            // 4) Prefetch latest LeaveBalance per employee (pick highest Id as "latest")
            var leaveBalancesRows = await _context.LeaveBalances
                .AsNoTracking()
                .Where(lb => employeeIds.Contains(lb.EmployeeId))
                .ToListAsync();

            var leaveBalanceMap = leaveBalancesRows
                .Where(lb => lb.EmployeeId != 0)
                .GroupBy(lb => lb.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(lb => lb.Id).FirstOrDefault());

            // 5) Map liabilities -> enriched DTOs
            var result = liabilities.Select(l =>
            {
                headcountMap.TryGetValue(l.EmployeeId, out var hc);
                leaveBalanceMap.TryGetValue(l.EmployeeId, out var lb);

                var name = (hc != null) ? string.Join(" ", new[] { hc.FirstName, hc.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) : null;

                return new EmployeeLiabilityFullDto
                {
                    // core
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

                    // extras from Headcount & LeaveBalance
                    EmployeeName = name,
                    Position = hc?.PositionTitle,
                    PayCategory = hc?.SalariedOrWaged,         // rename in DTO as PayCategory
                    OrganizationalUnit = hc?.OrganizationalUnit,
                    Function = hc?.OrganizationalKey,          // as you requested
                    Location = hc?.Location,
                    BusinessUnit = hc?.BusinessUnit,
                    NextAnniversaryDate = lb?.ALNextAnniversary,
                    ManagerName = hc?.NameOfSuperior,
                    WeeklyHours = hc?.WeeklyHours
                };
            })
            // optional: sort by LiabilityAmount desc so frontend gets useful order by default
            .OrderByDescending(x => x.LiabilityAmount)
            .ToList();

            return Ok(result);
        }

    }
}
