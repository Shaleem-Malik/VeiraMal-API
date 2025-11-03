namespace VeiraMal.API.DTOs
{
    public class CompanyUpdateDto
    {
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }

        // NEW
        public string? Location { get; set; }
    }
}
