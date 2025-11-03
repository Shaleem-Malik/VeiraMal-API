namespace VeiraMal.API.DTOs
{
    public class SaveAnalysisRequest
    {
        public int Year { get; set; }   // chosen by user
        public int Month { get; set; }  // chosen by user (1-12)

        public object? Headcount { get; set; }
        public object? NHT { get; set; }
        public object? Terms { get; set; }
        public bool IsFinal { get; set; } //Flag
    }
}
