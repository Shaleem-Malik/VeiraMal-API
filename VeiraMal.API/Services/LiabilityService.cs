using Microsoft.EntityFrameworkCore;
using System.Globalization;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class LiabilityService : ILiabilityService
    {
        private readonly AppDbContext _context;
        public LiabilityService(AppDbContext context) => _context = context;

        public async Task<string> CalculateLiabilitiesAsync(int sourceBatchId)
        {
            var today = DateTime.UtcNow.Date;

            // HEADCOUNT master
            var employees = await _context.Headcounts.AsNoTracking().ToListAsync();

            // LEAVE BALANCES - load batch, ignore EmployeeId == 0, handle duplicates
            var leaveBalancesList = await _context.LeaveBalances
                .Where(lb => lb.UploadBatchId == sourceBatchId)
                .AsNoTracking()
                .ToListAsync();

            var leaveBalances = leaveBalancesList
                .Where(lb => lb.EmployeeId != 0)
                .GroupBy(lb => lb.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // BASE RATES - safe grouping
            var baseRatesList = await _context.BaseRates.AsNoTracking().ToListAsync();
            var baseRates = baseRatesList
                .Where(br => br.EmployeeId != 0)
                .GroupBy(br => br.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // FUTURE LEAVES - use same upload batch + current year + start >= today
            var futureLeaves = await _context.LeaveTakens
                .Where(l => l.UploadBatchId == sourceBatchId && l.StartDate >= today && l.StartDate.Year == today.Year)
                .AsNoTracking()
                .ToListAsync();

            var liabilities = new List<EmployeeLiability>();

            foreach (var e in employees)
            {
                int roundedBalance = 0;
                if (leaveBalances.TryGetValue(e.PersonnelNumber, out var lb)) roundedBalance = lb.RoundedBalanceDays;

                int entitlement = 20;
                int totalLeaveBalance = roundedBalance + entitlement;

                decimal futureBooked = futureLeaves
                    .Where(l => l.PersonnelNumber == e.PersonnelNumber)
                    .Sum(l => (decimal?)l.Days) ?? 0m;

                int targetDays;
                if (totalLeaveBalance > 50) targetDays = 40;
                else if (totalLeaveBalance >= 31) targetDays = totalLeaveBalance - 10;
                else targetDays = 20;

                // DaysLeft: compute decimal then ceil to int (business choice)
                decimal daysLeftDecimal = targetDays - futureBooked;
                int daysLeftToTake = (int)Math.Max(Math.Ceiling(daysLeftDecimal), 0m);

                // DailyRate
                decimal dailyRate = 0m;
                if (baseRates.TryGetValue(e.PersonnelNumber, out var br))
                {
                    if (br.RateUnit == RateUnit.Weekly)
                    {
                        dailyRate = br.Rate / 5m;
                    }
                    else // Hourly
                    {
                        decimal weeklyHoursDecimal = TryParseDecimal(e.WeeklyHours);
                        dailyRate = br.Rate * (weeklyHoursDecimal / 5m);
                    }
                }
                else
                {
                    // optional: log or flag missing base rate for this employee
                }

                decimal liabilityAmount = dailyRate * targetDays;
                int benefitDays = targetDays - entitlement;
                decimal benefitAmount = Math.Max(dailyRate * Math.Max(benefitDays, 0), 0m);

                liabilities.Add(new EmployeeLiability
                {
                    EmployeeId = e.PersonnelNumber,
                    CalculationDate = today,
                    BalanceDays = roundedBalance,
                    EntitlementDays = entitlement,
                    TotalLeaveBalance = totalLeaveBalance,
                    FutureLeaveBookedDays = futureBooked,
                    TargetDays = targetDays,
                    DaysLeftToTake = daysLeftToTake,
                    DailyRate = decimal.Round(dailyRate, 2),
                    LiabilityAmount = decimal.Round(liabilityAmount, 2),
                    BenefitDays = benefitDays,
                    BenefitAmount = decimal.Round(benefitAmount, 2),
                    SourceUploadBatchId = sourceBatchId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Replace today's liabilities
            _context.EmployeeLiabilities.RemoveRange(_context.EmployeeLiabilities.Where(x => x.CalculationDate == today));
            await _context.SaveChangesAsync();

            await _context.EmployeeLiabilities.AddRangeAsync(liabilities);
            await _context.SaveChangesAsync();

            return $"Calculated liabilities for {liabilities.Count} employees (batch {sourceBatchId}).";
        }


        private decimal TryParseDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(s, out d)) return d;
            return 0m;
        }
    }

}
