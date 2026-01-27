using System;
using System.Collections.Generic;

namespace VeiraMal.API.DTOs
{
    public class UploadResultDto
    {
        public int UploadBatchId { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class EmployeeLiabilityDto
    {
        public int EmployeeId { get; set; }
        public DateTime CalculationDate { get; set; }
        public int BalanceDays { get; set; }
        public int EntitlementDays { get; set; }
        public int TotalLeaveBalance { get; set; }
        public decimal FutureLeaveBookedDays { get; set; }
        public int TargetDays { get; set; }
        public int DaysLeftToTake { get; set; }
        public decimal DailyRate { get; set; }
        public decimal LiabilityAmount { get; set; }
        public int BenefitDays { get; set; }
        public decimal BenefitAmount { get; set; }
    }

    public class TopEmployeeDto
    {
        public int EmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public decimal TotalLiability { get; set; }
    }

    public class DepartmentSummaryDto
    {
        public string Department { get; set; } = string.Empty;
        public decimal TotalLiability { get; set; }
        public decimal AverageLeaveBalance { get; set; }
        public int Headcount { get; set; }
    }

    public class RegionSummaryDto
    {
        public string Region { get; set; } = string.Empty;
        public decimal AverageLeaveBalance { get; set; }
        public int Headcount { get; set; }
    }

    public class EmployeeLiabilityFullDto
    {
        // Core liability fields (from EmployeeLiabilities)
        public int EmployeeId { get; set; }
        public DateTime CalculationDate { get; set; }
        public int BalanceDays { get; set; }
        public int EntitlementDays { get; set; }
        public int TotalLeaveBalance { get; set; }
        public decimal FutureLeaveBookedDays { get; set; }
        public int TargetDays { get; set; }
        public int DaysLeftToTake { get; set; }
        public decimal DailyRate { get; set; }
        public decimal LiabilityAmount { get; set; }
        public int BenefitDays { get; set; }
        public decimal BenefitAmount { get; set; }
        public int SourceUploadBatchId { get; set; }
        public DateTime CreatedAt { get; set; }

        // Extra fields requested (from Headcount / LeaveBalance)
        public string? EmployeeName { get; set; }           // FirstName + LastName (Headcount)
        public string? Position { get; set; }               // PositionTitle (Headcount)
        public string? PayCategory { get; set; }            // "Waged" / "Salaried" (Headcount.SalariedOrWaged)
        public string? OrganizationalUnit { get; set; }     // Headcount.OrganizationalUnit
        public string? Function { get; set; }               // Headcount.OrganizationalKey (as requested)
        public string? Location { get; set; }               // Headcount.Location
        public string? BusinessUnit { get; set; }           // Headcount.BusinessUnit
        public DateTime? NextAnniversaryDate { get; set; }  // LeaveBalance.ALNextAnniversary
        public string? ManagerName { get; set; }            // Headcount.NameOfSuperior
        public string? WeeklyHours { get; set; }            // Headcount.WeeklyHours (string in model)
    }
}
