using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.DTOs
{
    public class UpdateUserDto
    {
        [Required] public int UserId { get; set; }
        [Required] public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }       // NEW
        public string? LastName { get; set; }
        [Required][EmailAddress] public string Email { get; set; } = null!;
        public string? BusinessUnit { get; set; }
        public string? AccessLevel { get; set; }
        public bool IsActive { get; set; } = true;

        // NEW optional contact & location fields
        public string? ContactNumber { get; set; }    // NEW
        public string? Location { get; set; }         // NEW
    }
}
