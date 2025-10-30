using System;
using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.DTOs
{
    public class CreateSubCompanyDto
    {
        [Required]
        public string CompanyName { get; set; } = null!;
        public string? CompanyABN { get; set; }
        public string? ContactNumber { get; set; }
        public string? Location { get; set; }

        // zero or more parent-company superuser IDs to assign on creation
        public int[]? AssignedSuperUserIds { get; set; }
    }
}