using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Services.Admin;

public interface IAdminService
{
    Task<PaginatedResult<AdminUser>> GetUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<AdminUser> CreateUserAsync(AdminUserCreateRequest request, AdminContext context, CancellationToken cancellationToken);
    Task<AdminUser> UpdateUserAsync(string id, AdminUserUpdateRequest request, AdminContext context, CancellationToken cancellationToken);
    Task DeactivateUserAsync(string id, AdminContext context, CancellationToken cancellationToken);
    Task<PaginatedResult<AuditLog>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken);
    Task<SystemSettings> GetSettingsAsync(CancellationToken cancellationToken);
    Task<SystemSettings> UpdateSettingsAsync(SystemSettingsUpdate update, AdminContext context, CancellationToken cancellationToken);
}

public sealed record AdminContext(string UserId, string Role, string IpAddress);

public sealed record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
