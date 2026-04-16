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

        public async Task<string> CalculateLiabilitiesAsync()
        {
            // Current master data only
            var employees = await _context.Headcounts.AsNoTracking().ToListAsync();

            var leaveBalancesList = await _context.LeaveBalances
                .AsNoTracking()
                .ToListAsync();

            var leaveBalances = leaveBalancesList
                .Where(lb => lb.EmployeeId != 0)
                .GroupBy(lb => lb.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            var baseRatesList = await _context.BaseRates
                .AsNoTracking()
                .ToListAsync();

            var baseRates = baseRatesList
                .Where(br => br.EmployeeId != 0)
                .GroupBy(br => br.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            var todayStart = DateTime.UtcNow.Date;
            var futureLeaves = await _context.LeaveTakens
                .AsNoTracking()
                .Where(l => l.StartDate >= todayStart && l.StartDate.Year == todayStart.Year)
                .ToListAsync();

            var liabilities = new List<EmployeeLiability>();

            foreach (var e in employees)
            {
                int roundedBalance = 0;
                if (leaveBalances.TryGetValue(e.PersonnelNumber, out var lb))
                    roundedBalance = lb.RoundedBalanceDays;

                int entitlement = 20;
                int totalLeaveBalance = roundedBalance + entitlement;

                decimal futureBooked = futureLeaves
                    .Where(l => l.PersonnelNumber == e.PersonnelNumber)
                    .Sum(l => (decimal?)l.Days) ?? 0m;

                int targetDays;
                if (totalLeaveBalance > 50) targetDays = 40;
                else if (totalLeaveBalance >= 31) targetDays = totalLeaveBalance - 10;
                else targetDays = 20;

                decimal daysLeftDecimal = targetDays - futureBooked;
                int daysLeftToTake = (int)Math.Max(Math.Ceiling(daysLeftDecimal), 0m);

                decimal dailyRate = 0m;
                if (baseRates.TryGetValue(e.PersonnelNumber, out var br))
                {
                    if (br.RateUnit == RateUnit.Weekly)
                    {
                        dailyRate = br.Rate / 5m;
                    }
                    else
                    {
                        decimal weeklyHoursDecimal = TryParseDecimal(e.WeeklyHours);
                        dailyRate = br.Rate * (weeklyHoursDecimal / 5m);
                    }
                }

                decimal liabilityAmount = dailyRate * targetDays;
                int benefitDays = targetDays - entitlement;
                decimal benefitAmount = Math.Max(dailyRate * Math.Max(benefitDays, 0), 0m);

                liabilities.Add(new EmployeeLiability
                {
                    EmployeeId = e.PersonnelNumber,
                    CalculationDate = todayStart,
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
                    SourceUploadBatchId = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    // FULL REPLACE: remove all old liability rows
                    var existing = await _context.EmployeeLiabilities.ToListAsync();
                    if (existing.Any())
                    {
                        _context.EmployeeLiabilities.RemoveRange(existing);
                        await _context.SaveChangesAsync();
                    }

                    if (liabilities.Any())
                    {
                        await _context.EmployeeLiabilities.AddRangeAsync(liabilities);
                        await _context.SaveChangesAsync();
                    }

                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });

            return $"Calculated liabilities for {liabilities.Count} employees.";
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