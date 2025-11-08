using System;

namespace FIBRADIS.Application.Models.Admin;

public sealed record AdminUser
{
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = UserRoles.Viewer;
    public bool IsActive { get; init; }
        = true;
    public DateTime CreatedAtUtc { get; init; }
        = DateTime.UtcNow;
    public DateTime? LastLoginUtc { get; init; }
        = null;
}
