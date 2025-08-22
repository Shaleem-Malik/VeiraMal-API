namespace VeiraMal.API.ViewModels.Analytics
{
    public class PositionSalaryGapViewModel
    {
        public string PositionTitle { get; set; } = string.Empty;
        public decimal MaleAverageSalary { get; set; }
        public decimal FemaleAverageSalary { get; set; }
        public decimal SalaryGap { get; set; } // Male - Female
    }
}