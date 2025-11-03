using System;

namespace VeiraMal.API.Models
{
    public class Company
    {
        public Guid CompanyId { get; set; } = Guid.NewGuid();
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }

        // NEW: if this company is a subcompany, this points to the parent company
        public Guid? ParentCompanyId { get; set; }

        // Derived flag for convenience
        public bool IsSubcompany => ParentCompanyId.HasValue;

        // NEW: company location/address (optional)
        public string? Location { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Company? ParentCompany { get; set; }
        public ICollection<Company>? ChildCompanies { get; set; }

        // Superusers assigned to this company (many-to-many via CompanySuperUserAssignment)
        public ICollection<CompanySuperUserAssignment>? AssignedSuperUsers { get; set; }
    }
}
