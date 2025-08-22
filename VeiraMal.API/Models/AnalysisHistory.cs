namespace VeiraMal.API.Models
{
    public class AnalysisHistory
    {
        public int Id { get; set; }
        public DateTime AnalysisDate { get; set; }
        public string? HeadcountData { get; set; }
        public string? NHTData { get; set; }
        public string? TermsData { get; set; }
    }
}
