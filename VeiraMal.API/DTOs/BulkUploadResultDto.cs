namespace VeiraMal.API.DTOs
{
    public class BulkUploadResultDto
    {
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
