namespace VeiraMal.API.Models
{
    public class User
    {
        public int UserId { get; set; }
        public Guid UserGuid { get; set; } = Guid.NewGuid();
        public Guid CompanyId { get; set; }
        public int EmployeeNumber { get; set; }
        public string FirstName { get; set; } = null!;
        public string? LastName { get; set; }

        // optional middle name
        public string? MiddleName { get; set; }

        public string Email { get; set; } = null!;
        public string? PasswordHash { get; set; }

        // contact number for user (optional)
        public string? ContactNumber { get; set; }

        // user location/address (optional)
        public string? Location { get; set; }

        // legacy string fields kept for compatibility; also we provide the FK models below
        public string? BusinessUnit { get; set; }
        public string AccessLevel { get; set; } = "employee"; // or "superUser"
        public bool IsPasswordResetRequired { get; set; } = true;
        public bool IsActive { get; set; } = true;
        // true when user is initially created; set to false after first successful login
        public bool IsFirstLogin { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
