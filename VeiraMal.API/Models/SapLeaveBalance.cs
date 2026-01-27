namespace VeiraMal.API.Models
{
    public class SapLeaveBalance
    {
        public int Id { get; set; }
        public int PersonnelNumber { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime? ALNextAnniv { get; set; }
        public decimal ALBalance { get; set; }
        public decimal RoundedBalance { get; set; }
        public string? Function { get; set; }
        public DateTime ImportDate { get; set; }

        // Navigation property
        public virtual Headcount? Headcount { get; set; }
    }
}
