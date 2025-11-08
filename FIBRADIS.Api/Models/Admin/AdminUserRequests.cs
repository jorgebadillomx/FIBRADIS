using System.ComponentModel.DataAnnotations;

namespace FIBRADIS.Api.Models.Admin;

public sealed record CreateAdminUserRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}

public sealed record UpdateAdminUserRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    [MinLength(8)]
    public string? Password { get; init; }
}
