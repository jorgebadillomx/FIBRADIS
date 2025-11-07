using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryAuditService : IAuditService
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

    public IReadOnlyCollection<AuditLog> GetLogs() => _logs.ToArray();
}
