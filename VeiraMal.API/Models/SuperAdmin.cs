using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.Models
{
    public class SuperAdmin
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string? FullName { get; set; }

        [Required]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        public string? PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        //New field to store when password was last changed
        public DateTime? PasswordChangedAt { get; set; }
    }
}
