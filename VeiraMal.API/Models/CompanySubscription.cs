using System;

namespace VeiraMal.API.Models
{
    public class CompanySubscription
    {
        public Guid CompanySubscriptionId { get; set; } = Guid.NewGuid();

        // FK to company
        public Guid CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        // FK to plan (keeps reference)
        public Guid SubscriptionPlanId { get; set; }
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        // Snapshot fields at time of purchase (important for billing history)
        public string PlanNameSnapshot { get; set; } = null!;
        public int BaseUserSeatsSnapshot { get; set; }
        public int AdditionalSeatsPurchased { get; set; } = 0;
        public decimal AdditionalSeatPriceSnapshot { get; set; } = 0m;
        public decimal MonthlyPriceSnapshot { get; set; } = 0m; // base + additional

        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime? EndDate { get; set; } // if trial or end-of-subscription
    }
}
