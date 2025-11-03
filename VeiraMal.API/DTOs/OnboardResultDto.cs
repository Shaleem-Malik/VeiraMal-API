namespace VeiraMal.API.DTOs
{
    public class OnboardResultDto
    {
        public Guid CompanyId { get; set; }
        public int UserId { get; set; } // assuming your User.UserId is int
        public Guid CompanySubscriptionId { get; set; }
        public int AmountInCents { get; set; }
    }
}
