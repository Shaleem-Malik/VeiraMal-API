using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Services
{
    public class TokenBlacklistService : ITokenBlacklistService
    {
        private readonly AppDbContext _db;
        public TokenBlacklistService(AppDbContext db) => _db = db;

        public async Task RevokeTokenAsync(string jti, DateTime expiresAtUtc, int? userId = null, string? reason = null)
        {
            var exists = await _db.RevokedTokens.AnyAsync(r => r.Jti == jti);
            if (exists) return;

            _db.RevokedTokens.Add(new RevokedToken
            {
                Jti = jti,
                ExpiresAtUtc = expiresAtUtc,
                RevokedAtUtc = DateTime.UtcNow,
                UserId = userId,
                Reason = reason
            });

            await _db.SaveChangesAsync();
        }

        public async Task<bool> IsTokenRevokedAsync(string jti)
        {
            if (string.IsNullOrWhiteSpace(jti)) return false;
            // Optionally remove expired revoked tokens periodically (not done here)
            return await _db.RevokedTokens.AnyAsync(r => r.Jti == jti);
        }
    }
}
