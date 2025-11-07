using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Infrastructure.Observability.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Monitoring.HealthChecks;

public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly ISqlServerHealthProbe _probe;

    public SqlServerHealthCheck(ISqlServerHealthProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccessful)
        {
            var duration = result.Duration ?? TimeSpan.Zero;
            return HealthCheckResult.Healthy(result.Message ?? "SQL Server disponible", new Dictionary<string, object>
            {
                ["duration"] = duration.TotalMilliseconds
            });
        }

        return HealthCheckResult.Unhealthy(result.Message ?? "No se pudo conectar a SQL Server");
    }
}
