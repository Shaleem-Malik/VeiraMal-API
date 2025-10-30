namespace VeiraMal.API.DTOs
{
    public class AuthResponseDto
    {
        public string Token { get; set; } = null!;
        public bool MustResetPassword { get; set; }
        public string Message { get; set; } = null!;
        public bool IsFirstLogin { get; set; }

        // parsed business units (e.g. ["Finance","Commercial"])
        public List<string> BusinessUnits { get; set; } = new List<string>();

    }
}
