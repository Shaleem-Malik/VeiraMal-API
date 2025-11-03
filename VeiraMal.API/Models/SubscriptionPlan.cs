using System;

namespace VeiraMal.API.Models
{
    public class SubscriptionPlan
    {
        public Guid SubscriptionPlanId { get; set; } = Guid.NewGuid();
        public string Package { get; set; } = null!;         // e.g., "Starter"
        public decimal? PricePerMonth { get; set; }          // null => custom / contact sales
        public string IdealFor { get; set; } = null!;
        public string KeyFeatures { get; set; } = null!;
        public int BaseUserSeats { get; set; }               // 0 may mean "unlimited" for enterprise rows
        public int? MaxHC { get; set; }                      // headcount cap, null = unlimited
        public int SuperUsers { get; set; }                  // included superusers
        public bool AdditionalSeatsAllowed { get; set; }     // can buy extra seats at signup
        public decimal? AdditionalSeatPrice { get; set; }    // per extra user per month (null if N/A)
        public bool HasApi { get; set; } = false;
        public string ReportingLevel { get; set; } = "Basic"; // Basic/Advanced/Premium/Overview
    }
}
