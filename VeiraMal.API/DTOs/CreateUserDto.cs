using System.ComponentModel.DataAnnotations;

namespace VeiraMal.API.DTOs
{
    public class CreateUserDto
    {
        [Required] public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }            // NEW
        public string? LastName { get; set; }
        [Required][EmailAddress] public string Email { get; set; } = null!;
        public string? BusinessUnit { get; set; }
        public string? AccessLevel { get; set; }
        public bool ForcePasswordReset { get; set; } = true; // default true

        // NEW optional contact & location for the user
        public string? ContactNumber { get; set; }
        public string? Location { get; set; }
    }
}
