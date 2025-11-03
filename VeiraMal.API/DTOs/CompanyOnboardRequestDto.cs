namespace VeiraMal.API.DTOs
{
    public class CompanyOnboardRequestDto
    {
        public CompanyOnboardDto? Dto { get; set; }
        public string SuccessUrl { get; set; } = null!; // e.g. https://yourapp.com/checkout-success
        public string CancelUrl { get; set; } = null!; // e.g. https://yourapp.com/checkout-cancel
        public string SignInUrl { get; set; } = null!; // sign-in base url to include in email later
        public string? Currency { get; set; } = "aud";
    }
}
