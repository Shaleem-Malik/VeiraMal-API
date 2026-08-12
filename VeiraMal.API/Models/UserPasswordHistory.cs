namespace VeiraMal.API.Models
{
    public class UserPasswordHistory
    {
        public int UserPasswordHistoryId { get; set; }
        public int UserId { get; set; }
        public string PasswordHash { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;
    }
}
