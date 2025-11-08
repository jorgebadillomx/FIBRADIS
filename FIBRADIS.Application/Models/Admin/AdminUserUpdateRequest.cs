namespace FIBRADIS.Application.Models.Admin;

public sealed record AdminUserUpdateRequest(string Email, string Role, bool IsActive, string? Password);
