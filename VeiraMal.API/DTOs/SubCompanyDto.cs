using System;

namespace VeiraMal.API.DTOs
{
    public class SubCompanyDto
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }
        public string? Location { get; set; }
        public Guid ParentCompanyId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}