using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models;

public class HrEngagementScore
{
    [Key]
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public string EmployeeId { get; set; } = null!;
    public decimal Score { get; set; }
    public DateTime SurveyDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
