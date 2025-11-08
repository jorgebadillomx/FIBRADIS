using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryAuditService : IAuditService, IAuditLogReader
{
    private readonly ConcurrentBag<AuditLog> _logs = new();
    private readonly ILogger<InMemoryAuditService> _logger;

    public InMemoryAuditService(ILogger<InMemoryAuditService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        var log = new AuditLog
        {
            UserId = entry.UserId,
            Action = entry.Action,
            Result = entry.Result,
            IpAddress = entry.IpAddress,
            Metadata = entry.Metadata,
            TimestampUtc = DateTime.UtcNow
        };
        _logs.Add(log);
        _logger.LogInformation("Audit event {Action} for {UserId} result {Result}", entry.Action, entry.UserId, entry.Result);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var enumerable = _logs.ToArray().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            enumerable = enumerable.Where(l => string.Equals(l.UserId, query.UserId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            enumerable = enumerable.Where(l => string.Equals(l.Action, query.Action, StringComparison.OrdinalIgnoreCase));
        }

        if (query.FromUtc is not null)
        {
            enumerable = enumerable.Where(l => l.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc is not null)
        {
            enumerable = enumerable.Where(l => l.TimestampUtc <= query.ToUtc.Value);
        }

        var ordered = enumerable.OrderByDescending(l => l.TimestampUtc).ToArray();
        var total = ordered.Length;
        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return Task.FromResult<(IReadOnlyList<AuditLog>, int)>((items, total));
    }
}
