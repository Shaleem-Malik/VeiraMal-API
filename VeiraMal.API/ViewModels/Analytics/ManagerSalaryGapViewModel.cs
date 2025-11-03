namespace VeiraMal.API.ViewModels.Analytics
{
    public class ManagerSalaryGapViewModel
    {
        public string ManagerEmployeeId { get; set; } = string.Empty;
        public decimal AverageSalaryMale { get; set; }
        public decimal AverageSalaryFemale { get; set; }
        public decimal SalaryGap { get; set; } // Male - Female
    }
}