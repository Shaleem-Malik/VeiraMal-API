using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VeiraMal.API.Models
{
    public class EmployeeLiability
    {
        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }               // PersonnelNumber
        public DateTime CalculationDate { get; set; }     // date of calculation

        public int BalanceDays { get; set; }              // Rounded balance from SAP
        public int EntitlementDays { get; set; }          // default 20
        public int TotalLeaveBalance { get; set; }        // Rounded + Entitlement

        public decimal FutureLeaveBookedDays { get; set; }
        public int TargetDays { get; set; }
        public int DaysLeftToTake { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DailyRate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal LiabilityAmount { get; set; }

        public int BenefitDays { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BenefitAmount { get; set; }

        public int SourceUploadBatchId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
