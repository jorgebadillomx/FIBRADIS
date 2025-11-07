using System;

namespace FIBRADIS.Application.Models.Auth;

public sealed record UserSecret
{
    public string UserId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string EncryptedKey { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; init; } = null;
}
