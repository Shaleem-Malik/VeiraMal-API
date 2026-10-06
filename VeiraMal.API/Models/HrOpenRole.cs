using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models;

public class HrOpenRole
{
    [Key]
    public long Id { get; set; }

    public Guid CompanyId { get; set; }
    public string Department { get; set; } = "Unassigned";
    public string? RoleTitle { get; set; }
    public int OpenRoles { get; set; } = 1;
    public string? Status { get; set; }
    public DateTime? AsOfDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
