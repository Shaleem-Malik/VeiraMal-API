using System;

namespace VeiraMal.API.Models
{
    public class CompanySuperUserAssignment
    {
        // The CompanyId here is the subcompany's CompanyId (child)
        public Guid CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        // The UserId is a user of the parent company (must have AccessLevel = "superUser")
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}
