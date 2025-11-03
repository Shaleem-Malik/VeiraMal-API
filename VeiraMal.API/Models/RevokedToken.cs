using System;

namespace VeiraMal.API.Models
{
    public class RevokedToken
    {
        public int Id { get; set; }
        public string Jti { get; set; } = null!;
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime RevokedAtUtc { get; set; }
        public int? UserId { get; set; }
        public string? Reason { get; set; }
    }
}
