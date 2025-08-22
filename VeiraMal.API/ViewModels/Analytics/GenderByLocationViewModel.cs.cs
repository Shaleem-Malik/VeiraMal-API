namespace VeiraMal.API.ViewModels.Analytics
{
    public class GenderByLocationViewModel
    {
        public string Location { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}