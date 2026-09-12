using System;
using System.Linq;

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

        // Parent-company Superusers currently assigned
        // to manage this subcompany.
        public int[] AssignedSuperUserIds { get; set; } = Array.Empty<int>();
    }
}