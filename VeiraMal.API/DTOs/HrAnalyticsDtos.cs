using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.DTOs;

public sealed class HrAnalyticsSettingsDto
{
    public int Id { get; init; }
    public decimal TurnoverHealthyThreshold { get; init; }
    public decimal TurnoverWatchThreshold { get; init; }
    public decimal AbsenceHealthyThreshold { get; init; }
    public decimal AbsenceWatchThreshold { get; init; }
    public int WorkingDaysPerYear { get; init; }
    public int RollingAverageWindowMonths { get; init; }
    public string Currency { get; init; } = "USD";
    public int RequisitionAgeDays { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class HrAnalyticsSettingsUpdateDto
{
    [Range(0, 100)]
    public decimal TurnoverHealthyThreshold { get; set; } = 10m;

    [Range(0, 100)]
    public decimal TurnoverWatchThreshold { get; set; } = 15m;

    [Range(0, 100)]
    public decimal AbsenceHealthyThreshold { get; set; } = 2.5m;

    [Range(0, 100)]
    public decimal AbsenceWatchThreshold { get; set; } = 4.5m;

    [Range(1, 366)]
    public int WorkingDaysPerYear { get; set; } = 260;

    [Range(1, 12)]
    public int RollingAverageWindowMonths { get; set; } = 3;

    [Required, StringLength(10)]
    public string Currency { get; set; } = "USD";

    [Range(1, 3650)]
    public int RequisitionAgeDays { get; set; } = 45;
}


public sealed class HrDashboardDto
{
    public DateTime ReportingDate { get; init; }
    public string ReportingMonth { get; init; } = string.Empty;
    public HrAnalyticsSettingsDto Settings { get; init; } = new();
    public HrKpiDto Kpis { get; init; } = new();
    public List<HrMonthlyTrendDto> HeadcountTrend { get; init; } = new();
    public List<HrMonthlyHiresExitsDto> HiresVsExits { get; init; } = new();
    public List<HrDepartmentDto> Departments { get; init; } = new();
    public HrDiversityDto Diversity { get; init; } = new();
    public HrLabourCostDto LabourCost { get; init; } = new();
    public HrAttritionDto Attrition { get; init; } = new();
    public HrAbsenteeismDto Absenteeism { get; init; } = new();
    public List<HrActivityDto> RecentActivity { get; init; } = new();
    public List<HrEmployeeSearchDto> RiskWatchlist { get; init; } = new();
}

public sealed class HrKpiDto
{
    public int ActiveHeadcount { get; init; }
    public decimal ActiveFte { get; init; }
    public decimal AverageTenureYears { get; init; }
    public int Hires { get; init; }
    public int Exits { get; init; }
    public decimal TurnoverRate { get; init; }
    public string TurnoverStatus { get; init; } = "Healthy";
    public decimal LabourCost { get; init; }
    public decimal AverageSalary { get; init; }
    public decimal CostPerHead { get; init; }
    public decimal FemalePercentage { get; init; }
    public int OpenRoles { get; init; }
    public decimal AbsenceRate { get; init; }
    public string AbsenceStatus { get; init; } = "Healthy";
    public decimal UnplannedLeaveDays { get; init; }
}

public sealed class HrMonthlyTrendDto
{
    public string Label { get; init; } = string.Empty;
    public DateTime Month { get; init; }
    public int Headcount { get; init; }
    public decimal Fte { get; init; }
    public decimal RollingAverageHeadcount { get; init; }
}

public sealed class HrMonthlyHiresExitsDto
{
    public string Label { get; init; } = string.Empty;
    public DateTime Month { get; init; }
    public int Hires { get; init; }
    public int Exits { get; init; }
}

public sealed class HrDepartmentDto
{
    public string Name { get; init; } = string.Empty;
    public int Headcount { get; init; }
    public decimal Fte { get; init; }
    public decimal LabourCost { get; init; }
    public decimal AverageSalary { get; init; }
    public decimal Turnover { get; init; }
    public string TurnoverStatus { get; init; } = "Healthy";
    public decimal FemalePercentage { get; init; }
    public decimal AbsenceRate { get; init; }
    public string AbsenceStatus { get; init; } = "Healthy";
}

public sealed class HrDiversityDto
{
    public List<HrBreakdownItemDto> Gender { get; init; } = new();
    public List<HrBreakdownItemDto> EmploymentType { get; init; } = new();
    public List<HrBreakdownItemDto> WorkArrangement { get; init; } = new();
    public List<HrBreakdownItemDto> Age { get; init; } = new();
    public List<HrBreakdownItemDto> Tenure { get; init; } = new();
    public List<HrBreakdownItemDto> Location { get; init; } = new();
    public List<HrBreakdownItemDto> Ethnicity { get; init; } = new();
    public List<HrGenderDepartmentDto> GenderByDepartment { get; init; } = new();
}

public sealed class HrBreakdownItemDto
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

public sealed class HrGenderDepartmentDto
{
    public string Department { get; init; } = string.Empty;
    public int Male { get; init; }
    public int Female { get; init; }
    public int Other { get; init; }
}

public sealed class HrLabourCostDto
{
    public decimal TotalAnnualBaseSalary { get; init; }
    public decimal MonthlyEquivalent { get; init; }
    public List<HrBreakdownItemDto> CostByDepartment { get; init; } = new();
    public List<HrBreakdownItemDto> SalaryDistribution { get; init; } = new();
    public List<HrDepartmentSalaryDto> AverageSalaryByDepartment { get; init; } = new();
    public List<HrBreakdownItemDto> CostByEmploymentType { get; init; } = new();
    public List<HrBreakdownItemDto> CostByLocation { get; init; } = new();
}

public sealed class HrDepartmentSalaryDto
{
    public string Department { get; init; } = string.Empty;
    public decimal AverageSalary { get; init; }
}

public sealed class HrAttritionDto
{
    public decimal TurnoverRate { get; init; }
    public string TurnoverStatus { get; init; } = "Healthy";
    public decimal VoluntaryExitPercentage { get; init; }
    public decimal AverageTenureAtExitYears { get; init; }
    public decimal FirstHalfTurnover { get; init; }
    public decimal SecondHalfTurnover { get; init; }
    public decimal TurnoverDelta { get; init; }
    public List<HrMonthlyTurnoverDto> Trend { get; init; } = new();
    public List<HrBreakdownItemDto> ByDepartment { get; init; } = new();
    public List<HrBreakdownItemDto> ExitReasons { get; init; } = new();
    public List<HrBreakdownItemDto> TenureAtExit { get; init; } = new();
}

public sealed class HrMonthlyTurnoverDto
{
    public string Label { get; init; } = string.Empty;
    public DateTime Month { get; init; }
    public decimal Rate { get; init; }
}

public sealed class HrAbsenteeismDto
{
    public decimal TotalLeaveDays { get; init; }
    public decimal UnplannedLeaveDays { get; init; }
    public decimal AbsenceRate { get; init; }
    public string Status { get; init; } = "Healthy";
    public decimal SickDays { get; init; }
    public List<HrBreakdownItemDto> ByLeaveType { get; init; } = new();
    public List<HrBreakdownItemDto> PlannedVsUnplanned { get; init; } = new();
    public List<HrBreakdownItemDto> ByLocation { get; init; } = new();
    public List<HrDepartmentAbsenceDto> ByDepartment { get; init; } = new();
    public List<HrMonthlyAbsenceDto> Trend { get; init; } = new();
}

public sealed class HrDepartmentAbsenceDto
{
    public string Department { get; init; } = string.Empty;
    public int Headcount { get; init; }
    public decimal TotalLeaveDays { get; init; }
    public decimal UnplannedLeaveDays { get; init; }
    public decimal AbsenceRate { get; init; }
}

public sealed class HrMonthlyAbsenceDto
{
    public string Label { get; init; } = string.Empty;
    public DateTime Month { get; init; }
    public decimal UnplannedLeaveDays { get; init; }
}

public sealed class HrActivityDto
{
    public string Type { get; init; } = string.Empty;
    public DateTime Date { get; init; }
    public string Department { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class HrEmployeeSearchDto
{
    public string EmployeeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Department { get; init; }
    public string? Position { get; init; }
    public string? Email { get; init; }
    public decimal? Engagement { get; init; }
    public decimal TenureYears { get; init; }
    public string? Risk { get; init; }
    public int? RiskScore { get; init; }
}

public sealed class HrImportResultDto
{
    public int EmployeeRecords { get; init; }
    public int EngagementRecords { get; init; }
    public int OpenRoleRecords { get; init; }
    public int LeaveRecords { get; init; }
    public int WarningCount { get; init; }
    public List<string> Warnings { get; init; } = new();
    public int UploadBatchId { get; init; }
}
