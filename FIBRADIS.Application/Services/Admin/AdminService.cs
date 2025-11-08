using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Services.Admin;

public sealed class AdminService : IAdminService
{
    private readonly IAdminUserStore _userStore;
    private readonly IAuditService _auditService;
    private readonly IAuditLogReader _auditLogReader;
    private readonly ISystemSettingsStore _settingsStore;
    private readonly IAdminMetricsRecorder _metrics;

    public AdminService(
        IAdminUserStore userStore,
        IAuditService auditService,
        IAuditLogReader auditLogReader,
        ISystemSettingsStore settingsStore,
        IAdminMetricsRecorder metrics)
    {
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _auditLogReader = auditLogReader ?? throw new ArgumentNullException(nameof(auditLogReader));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async Task<PaginatedResult<AdminUser>> GetUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        EnsurePagination(ref page, ref pageSize);
        var (users, total) = await _userStore.ListAsync(search, page, pageSize, cancellationToken).ConfigureAwait(false);
        await RefreshActiveUsersMetricAsync(cancellationToken).ConfigureAwait(false);
        return new PaginatedResult<AdminUser>(users, page, pageSize, total);
    }

    public async Task<AdminUser> CreateUserAsync(AdminUserCreateRequest request, AdminContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        EnsureAdmin(context);
        ValidateRole(request.Role);
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password is required", nameof(request));
        }

        var created = await _userStore.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        await RefreshActiveUsersMetricAsync(cancellationToken).ConfigureAwait(false);
        await RecordAuditAsync(context, "admin.createUser", "success", new Dictionary<string, object?>
        {
            ["userId"] = created.UserId,
            ["role"] = created.Role,
            ["email"] = created.Email
        }, cancellationToken).ConfigureAwait(false);
        _metrics.RecordRoleChange("none", created.Role);
        return created;
    }

    public async Task<AdminUser> UpdateUserAsync(string id, AdminUserUpdateRequest request, AdminContext context, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        EnsureAdmin(context);
        ValidateRole(request.Role);

        var existing = await _userStore.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                      ?? throw new KeyNotFoundException($"User '{id}' was not found");

        if (!string.Equals(existing.Role, request.Role, StringComparison.OrdinalIgnoreCase))
        {
            _metrics.RecordRoleChange(existing.Role, request.Role);
        }

        var updated = await _userStore.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        await RefreshActiveUsersMetricAsync(cancellationToken).ConfigureAwait(false);

        await RecordAuditAsync(context, "admin.updateUser", "success", new Dictionary<string, object?>
        {
            ["userId"] = id,
            ["previousRole"] = existing.Role,
            ["newRole"] = updated.Role,
            ["isActive"] = updated.IsActive
        }, cancellationToken).ConfigureAwait(false);

        return updated;
    }

    public async Task DeactivateUserAsync(string id, AdminContext context, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(context);
        EnsureAdmin(context);

        await _userStore.DeactivateAsync(id, cancellationToken).ConfigureAwait(false);
        await RefreshActiveUsersMetricAsync(cancellationToken).ConfigureAwait(false);

        await RecordAuditAsync(context, "admin.deactivateUser", "success", new Dictionary<string, object?>
        {
            ["userId"] = id
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PaginatedResult<AuditLog>> GetAuditLogsAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = query.Page;
        var pageSize = query.PageSize;
        EnsurePagination(ref page, ref pageSize);
        var normalizedQuery = query with { Page = page, PageSize = pageSize };
        var (logs, total) = await _auditLogReader.GetAsync(normalizedQuery, cancellationToken).ConfigureAwait(false);
        _metrics.SetActiveUsers(await _userStore.CountActiveAsync(cancellationToken).ConfigureAwait(false));
        return new PaginatedResult<AuditLog>(logs, normalizedQuery.Page, normalizedQuery.PageSize, total);
    }

    public Task<SystemSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return _settingsStore.GetAsync(cancellationToken);
    }

    public async Task<SystemSettings> UpdateSettingsAsync(SystemSettingsUpdate update, AdminContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(context);
        EnsureAdmin(context);

        var settings = await _settingsStore.UpdateAsync(update, cancellationToken).ConfigureAwait(false);
        _metrics.IncrementSettingsChanges();

        await RecordAuditAsync(context, "admin.updateSettings", "success", new Dictionary<string, object?>
        {
            ["marketHoursMx"] = settings.MarketHoursMx,
            ["llmMaxTokensPerUser"] = settings.LlmMaxTokensPerUser,
            ["securityMaxUploadSize"] = settings.SecurityMaxUploadSize,
            ["maintenanceMode"] = settings.SystemMaintenanceMode
        }, cancellationToken).ConfigureAwait(false);

        if (settings.SystemMaintenanceMode)
        {
            _metrics.NotifyMaintenanceModeEnabled();
        }

        return settings;
    }

    private static void ValidateRole(string role)
    {
        if (!UserRoles.IsValid(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Invalid role");
        }
    }

    private static void EnsureAdmin(AdminContext context)
    {
        if (!string.Equals(context.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Admin privileges required");
        }
    }

    private async Task RefreshActiveUsersMetricAsync(CancellationToken cancellationToken)
    {
        var activeCount = await _userStore.CountActiveAsync(cancellationToken).ConfigureAwait(false);
        _metrics.SetActiveUsers(activeCount);
    }

    private static void EnsurePagination(ref int page, ref int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 20;
        }

        pageSize = Math.Min(pageSize, 100);
    }

    private async Task RecordAuditAsync(AdminContext context, string action, string result, IReadOnlyDictionary<string, object?> metadata, CancellationToken cancellationToken)
    {
        await _auditService.RecordAsync(new AuditEntry(context.UserId, action, result, context.IpAddress, metadata), cancellationToken).ConfigureAwait(false);
        _metrics.IncrementAuditEntries();
    }
}
