namespace VeiraMal.API.Models
{
    public class NotificationQueue
    {
        public Guid NotificationId { get; set; } = Guid.NewGuid();
        public int? CompanyId { get; set; }
        public int? UserId { get; set; }
        public string NotificationType { get; set; } = null!; // Invite, Welcome, Reset
        public string? Payload { get; set; } // JSON payload (subject, body, attempts, ...)
        public int Attempts { get; set; } = 0;
        public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? NextAttemptAt { get; set; }
    }
}
