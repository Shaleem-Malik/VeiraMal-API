namespace VeiraMal.API.DTOs
{
    public class ImpersonationRequestDto
    {
        public Guid CompanyId { get; set; }
        public int? UserId { get; set; }
        public int ExpiresMinutes { get; set; } = 5;
        public string? RedirectPath { get; set; }
    }
}
