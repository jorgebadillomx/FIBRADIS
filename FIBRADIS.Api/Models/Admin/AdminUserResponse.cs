using System;
using FIBRADIS.Application.Models.Admin;

namespace FIBRADIS.Api.Models.Admin;

public sealed record AdminUserResponse(
    string UserId,
    string Username,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginUtc)
{
    public static AdminUserResponse FromModel(AdminUser user)
    {
        return new AdminUserResponse(
            user.UserId,
            user.Username,
            user.Email,
            user.Role,
            user.IsActive,
            user.CreatedAtUtc,
            user.LastLoginUtc);
    }
}
