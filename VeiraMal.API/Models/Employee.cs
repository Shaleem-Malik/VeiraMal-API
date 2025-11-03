namespace VeiraMal.API.Models
{
    public class Employee
    {
        public int Id { get; set; } // Auto-increment primary key (DB ID)

        public string? EmployeeId { get; set; }           // E001, E002, etc.
        public string? Gender { get; set; }
        public decimal BaseSalary { get; set; }
        public decimal TotalRemuneration { get; set; }
        public decimal SuperPercentage { get; set; }     // e.g. 10%

        public string? BusinessUnit { get; set; }
        public string? Department { get; set; }
        public string? OrgUnit { get; set; }
        public string? Location { get; set; }

        public DateTime DateOfBirth { get; set; }
        public DateTime HireDate { get; set; }

        public string? PositionTitle { get; set; }
        public string? ManagerEmployeeId { get; set; }

        public decimal FTE { get; set; }                 // Full-time equivalent (1.0, 0.8, etc.)
        public decimal HoursPerWeek { get; set; }
        public string? Level { get; set; }                // Could be string or int depending on data
    }
}
