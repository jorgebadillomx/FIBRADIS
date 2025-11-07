using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Application.Interfaces.Auth;

public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}

public sealed record AuditEntry(string UserId, string Action, string Result, string IpAddress, IReadOnlyDictionary<string, object?> Metadata);
