using System;

namespace FIBRADIS.Application.Models.Admin;

public sealed record AdminUserCreateRequest(string Email, string Role, string Password, bool IsActive)
{
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
