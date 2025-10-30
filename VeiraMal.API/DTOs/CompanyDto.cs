using System;

namespace VeiraMal.API.DTOs
{
    public class CompanyDto
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }

        // NEW
        public string? Location { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
