using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models
{
    public enum RateUnit
    {
        Weekly,
        Hourly
    }
    public class BaseRate
    {
        [Key]
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public decimal Rate { get; set; } = 0m;           // hourly or weekly based on RateUnit
        public RateUnit RateUnit { get; set; } = RateUnit.Weekly;
        public bool IsDefault { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
