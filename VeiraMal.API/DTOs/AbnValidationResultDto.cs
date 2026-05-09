namespace VeiraMal.API.DTOs
{
    public class AbnValidationResultDto
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Abn { get; set; }
        public string? EntityName { get; set; }
        public string? Status { get; set; }
    }
}
