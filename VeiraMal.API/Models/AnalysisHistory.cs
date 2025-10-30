namespace VeiraMal.API.Models
{
    public class AnalysisHistory
    {
        public int Id { get; set; }

        // Business period (selected by user)
        public int Year { get; set; }
        public int Month { get; set; }

        // System timestamp for auditing
        public DateTime CreatedAt { get; set; }

        public string? HeadcountData { get; set; }
        public string? NHTData { get; set; }
        public string? TermsData { get; set; }
        public bool IsFinal { get; set; } //flag
    }
}