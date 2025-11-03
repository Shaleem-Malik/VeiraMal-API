namespace VeiraMal.API.Models
{
    public class OnboardingPending
    {
        public Guid OnboardingPendingId { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public Guid UserId { get; set; } // the superuser
        public Guid CompanySubscriptionId { get; set; }

        // WARNING: plain text here for demonstration only.
        // In production, encrypt this field (e.g. using Data Protection / KMS).
        public string TempPasswordPlain { get; set; } = null!;

        // Expiry so we can garbage-collect stale pending onboards
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(48);

        // Optionally, store the Stripe Checkout Session Id we created for reconciliation
        public string? StripeCheckoutSessionId { get; set; }
    }
}
