using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models;

public class HrAnalyticsSettings
{
    [Key]
    public int Id { get; set; }

    public Guid CompanyId { get; set; }

    public decimal TurnoverHealthyThreshold { get; set; } = 10m;
    public decimal TurnoverWatchThreshold { get; set; } = 15m;

    public decimal AbsenceHealthyThreshold { get; set; } = 2.5m;
    public decimal AbsenceWatchThreshold { get; set; } = 4.5m;

    public int WorkingDaysPerYear { get; set; } = 260;
    public int RollingAverageWindowMonths { get; set; } = 3;

    [MaxLength(10)]
    public string Currency { get; set; } = "USD";

    public int RequisitionAgeDays { get; set; } = 45;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
