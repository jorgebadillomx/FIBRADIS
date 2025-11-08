using System;
using System.Collections.Generic;

namespace FIBRADIS.Application.Models.Auth;

public sealed record UserAccount
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string Role { get; init; } = "viewer";
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; init; }
        = null;
    public string? PasswordSalt { get; init; } = null;
    public IReadOnlyCollection<string> Roles => new[] { Role };
}
