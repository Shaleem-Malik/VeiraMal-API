using System;
using System.Threading.Tasks;

namespace VeiraMal.API.Services.Interfaces
{
    public interface ITokenBlacklistService
    {
        Task RevokeTokenAsync(string jti, DateTime expiresAtUtc, int? userId = null, string? reason = null);
        Task<bool> IsTokenRevokedAsync(string jti);
    }
}
