using System;
using System.Collections.Concurrent;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Infrastructure.Observability.Metrics;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Api.Monitoring;

public sealed class AdminMetricsRecorder : IAdminMetricsRecorder
{
    private static readonly TimeSpan RoleChangeWindow = TimeSpan.FromHours(1);

    private readonly ObservabilityMetricsRegistry _metrics;
    private readonly ILogger<AdminMetricsRecorder> _logger;
    private readonly IClock _clock;
    private readonly ConcurrentQueue<DateTimeOffset> _roleChangeEvents = new();

    public AdminMetricsRecorder(
        ObservabilityMetricsRegistry metrics,
        ILogger<AdminMetricsRecorder> logger,
        IClock clock)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void SetActiveUsers(int activeUsers)
    {
        _metrics.SetAdminActiveUsers(activeUsers);
    }

    public void IncrementAuditEntries()
    {
        _metrics.IncrementAdminAuditEntries();
    }

    public void IncrementSettingsChanges()
    {
        _metrics.IncrementAdminSettingsChanges();
    }

    public void RecordRoleChange(string fromRole, string toRole)
    {
        var now = _clock.UtcNow;
        _roleChangeEvents.Enqueue(now);

        while (_roleChangeEvents.TryPeek(out var oldest) && now - oldest > RoleChangeWindow)
        {
            _roleChangeEvents.TryDequeue(out _);
        }

        var count = _roleChangeEvents.Count;
        if (count > 3)
        {
            _logger.LogWarning("ALERTA SEGURIDAD: {Count} cambios de rol en la última hora entre {FromRole} y {ToRole}", count, fromRole, toRole);
        }
    }

    public void NotifyMaintenanceModeEnabled()
    {
        _logger.LogWarning("ALERTA MANTENIMIENTO: MaintenanceMode activado. Notificar a Slack");
    }
}
