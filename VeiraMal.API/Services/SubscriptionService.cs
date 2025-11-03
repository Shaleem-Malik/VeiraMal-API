using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly AppDbContext _db;
        public SubscriptionService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync()
        {
            return await _db.SubscriptionPlans
                .Select(p => new SubscriptionPlanDto
                {
                    SubscriptionPlanId = p.SubscriptionPlanId,
                    Package = p.Package,
                    PricePerMonth = p.PricePerMonth,
                    IdealFor = p.IdealFor,
                    KeyFeatures = p.KeyFeatures,
                    BaseUserSeats = p.BaseUserSeats,
                    MaxHC = p.MaxHC,
                    SuperUsers = p.SuperUsers,
                    AdditionalSeatsAllowed = p.AdditionalSeatsAllowed,
                    AdditionalSeatPrice = p.AdditionalSeatPrice,
                    HasApi = p.HasApi,
                    ReportingLevel = p.ReportingLevel
                }).ToListAsync();
        }
    }
}
