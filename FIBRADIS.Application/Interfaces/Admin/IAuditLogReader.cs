using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Application.Interfaces.Admin;

public interface IAuditLogReader
{
    Task<(IReadOnlyList<AuditLog> Logs, int TotalCount)> GetAsync(AuditLogQuery query, CancellationToken cancellationToken);
}
