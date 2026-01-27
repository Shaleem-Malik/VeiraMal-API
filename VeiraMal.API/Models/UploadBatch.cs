using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models
{
    public class UploadBatch
    {
        [Key]
        public int Id { get; set; }
        public string? UploadedBy { get; set; }
        public string? FilesMeta { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Status { get; set; } = "Completed";
    }
}
