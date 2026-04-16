using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models
{
    public class LeaveTaken
    {
        [Key]
        public int Id { get; set; }

        public int PersonnelNumber { get; set; }          // Employee id (matches Headcount.PersonnelNumber)
        public string? PersonnelName { get; set; }
        public string? Position { get; set; }
        public string? PersonnelSubarea { get; set; }
        public string? PersonnelArea { get; set; }
        public string? Function { get; set; }
        public string? Manager { get; set; }
        public string? CostCentreNumber { get; set; }
        public string? CostCentreDescription { get; set; }
        public string? OrganizationalUnit { get; set; }
        public decimal EmploymentPercentage { get; set; }
        public decimal WeeklyHours { get; set; }
        public string? AttendanceAbsenceType { get; set; }
        public decimal Hours { get; set; }
        public decimal Days { get; set; }
        public DateTime StartDate { get; set; } = DateTime.MinValue;
        public DateTime EndDate { get; set; } = DateTime.MinValue;
        public DateTime ChangedOn { get; set; } = DateTime.MinValue;
        public string? Location { get; set; }
        public string? Month { get; set; }
        public string? EmploymentType { get; set; }
        public string? BusinessUnit { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}