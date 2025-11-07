using System;
using System.Collections.Generic;

namespace FIBRADIS.Application.Models.Auth;

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string IpAddress { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}
