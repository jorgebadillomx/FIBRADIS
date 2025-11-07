using System;

namespace FIBRADIS.Application.Models.Auth;

public sealed class RefreshToken
{
    public string Token { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddDays(7);
    public string CreatedByIp { get; init; } = string.Empty;
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }
}
