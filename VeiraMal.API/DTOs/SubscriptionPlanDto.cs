using System;

namespace VeiraMal.API.DTOs
{
    public class SubscriptionPlanDto
    {
        public Guid SubscriptionPlanId { get; set; }
        public string Package { get; set; } = null!;
        public decimal? PricePerMonth { get; set; }
        public string IdealFor { get; set; } = null!;
        public string KeyFeatures { get; set; } = null!;
        public int BaseUserSeats { get; set; }
        public int? MaxHC { get; set; }
        public int SuperUsers { get; set; }
        public bool AdditionalSeatsAllowed { get; set; }
        public decimal? AdditionalSeatPrice { get; set; }
        public bool HasApi { get; set; }
        public string ReportingLevel { get; set; } = null!;
    }
}
