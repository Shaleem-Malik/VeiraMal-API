namespace VeiraMal.API.DTOs
{
    public class CompanyOnboardDto
    {
        // superuser minimal required fields (existing)
        public string SuperUserEmail { get; set; } = null!;
        public string SuperUserFirstName { get; set; } = null!;
        public string? SuperUserLastName { get; set; }

        // NEW: optional middle name for superuser
        public string? SuperUserMiddleName { get; set; }

        // NEW: optional user contact number (if omitted we'll default to company contact)
        public string? SuperUserContactNumber { get; set; }

        // NEW: user location (optional; default to company location if empty)
        public string? SuperUserLocation { get; set; }

        // company fields
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }

        // NEW: company location/address
        public string? CompanyLocation { get; set; }

        // New fields for the subscription selection
        public Guid SubscriptionPlanId { get; set; }         // required for signup
        public int AdditionalSeatsRequested { get; set; } = 0; // optional; 0 default
    }
}
