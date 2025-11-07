using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Infrastructure.Observability.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Monitoring.HealthChecks;

public sealed class HangfireHealthCheck : IHealthCheck
{
    private readonly IBackgroundJobHealthProbe _probe;

    public HangfireHealthCheck(IBackgroundJobHealthProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccessful)
        {
            return HealthCheckResult.Healthy(result.Message ?? "Hangfire saludable", new Dictionary<string, object>
            {
                ["duration"] = (result.Duration ?? TimeSpan.Zero).TotalMilliseconds
            });
        }

        return HealthCheckResult.Unhealthy(result.Message ?? "Hangfire degradado");
    }
}
