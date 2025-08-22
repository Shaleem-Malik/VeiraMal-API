namespace VeiraMal.API.ViewModels.Analytics
{
    public class GenderByManagerViewModel
    {
        public string ManagerEmployeeId { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}