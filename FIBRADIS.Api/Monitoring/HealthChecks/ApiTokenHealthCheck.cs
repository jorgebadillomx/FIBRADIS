using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Infrastructure.Observability.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Monitoring.HealthChecks;

public sealed class ApiTokenHealthCheck : IHealthCheck
{
    private readonly IApiTokenHealthProbe _probe;

    public ApiTokenHealthCheck(IApiTokenHealthProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccessful)
        {
            return HealthCheckResult.Healthy(result.Message ?? "Tokens válidos");
        }

        return HealthCheckResult.Degraded(result.Message ?? "Token inválido o expirado");
    }
}
