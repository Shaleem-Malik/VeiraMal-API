using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
            // normalize to UTC date start (midnight UTC)
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);

            // HEADCOUNT master (load outside transaction)
            var employees = await _context.Headcounts.AsNoTracking().ToListAsync();

            // LEAVE BALANCES - load batch, ignore EmployeeId == 0, handle duplicates (outside transaction)
            var leaveBalancesList = await _context.LeaveBalances
                .Where(lb => lb.UploadBatchId == sourceBatchId)
                .AsNoTracking()
                .ToListAsync();

            var leaveBalances = leaveBalancesList
                .Where(lb => lb.EmployeeId != 0)
                .GroupBy(lb => lb.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // BASE RATES - safe grouping (outside transaction)
            var baseRatesList = await _context.BaseRates.AsNoTracking().ToListAsync();
            var baseRates = baseRatesList
                .Where(br => br.EmployeeId != 0)
                .GroupBy(br => br.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            // FUTURE LEAVES - use same upload batch + current year + start >= todayStart (outside transaction)
            var futureLeaves = await _context.LeaveTakens
                .Where(l => l.UploadBatchId == sourceBatchId && l.StartDate >= todayStart && l.StartDate.Year == todayStart.Year)
                .AsNoTracking()
                .ToListAsync();

            // Build liabilities list in memory (outside transaction)
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
                    CalculationDate = todayStart, // normalized date-only
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

            // Use EF Core's execution strategy so transaction + operations are retriable as one unit
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                // Everything inside this delegate can be retried by the strategy on transient failures.
                // Start a transaction here.
                using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Fetch & remove existing liabilities for the date range (we fetch inside the delegate to ensure consistency on retry)
                    var toRemove = await _context.EmployeeLiabilities
                        .Where(x => x.CalculationDate >= todayStart && x.CalculationDate < todayEnd)
                        .ToListAsync();

                    if (toRemove.Any())
                    {
                        _context.EmployeeLiabilities.RemoveRange(toRemove);
                        await _context.SaveChangesAsync();
                    }

                    // Bulk insert new liabilities (if any)
                    if (liabilities.Any())
                    {
                        await _context.EmployeeLiabilities.AddRangeAsync(liabilities);
                        await _context.SaveChangesAsync();
                    }

                    await tx.CommitAsync();
                }
                catch
                {
                    // on any exception roll back and rethrow to allow the execution strategy to retry if applicable
                    await tx.RollbackAsync();
                    throw;
                }
            });

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
