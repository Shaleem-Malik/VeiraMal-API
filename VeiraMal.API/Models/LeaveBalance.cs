using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VeiraMal.API.Models
{
    public class LeaveBalance
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }               // Personnel number
        public string? EmployeeName { get; set; }
        public DateTime? ALNextAnniversary { get; set; }
        public decimal ALBalance { get; set; }            // raw decimal days from SAP
        public int RoundedBalanceDays { get; set; }       // Math.Ceiling(ALBalance)
        public string? Function { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
