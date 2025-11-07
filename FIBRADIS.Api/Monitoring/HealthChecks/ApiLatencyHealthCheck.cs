using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FIBRADIS.Api.Monitoring.HealthChecks;

public sealed class ApiLatencyHealthCheck : IHealthCheck
{
    private readonly RequestMetricsCollector _collector;
    private readonly string _pathPrefix;
    private readonly TimeSpan _threshold;

    public ApiLatencyHealthCheck(RequestMetricsCollector collector, string pathPrefix, TimeSpan threshold)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _pathPrefix = pathPrefix ?? throw new ArgumentNullException(nameof(pathPrefix));
        _threshold = threshold;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var average = _collector.TryGetAverageDuration(entry => entry.Path.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase));
        if (average is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Sin datos suficientes"));
        }

        var duration = average.Value;
        var status = duration <= _threshold
            ? HealthStatus.Healthy
            : HealthStatus.Degraded;

        var description = duration <= _threshold
            ? "Latencia dentro de los límites"
            : $"Latencia superior al umbral de {_threshold.TotalMilliseconds} ms";

        var data = new Dictionary<string, object>
        {
            ["avgLatencyMs"] = Math.Round(duration.TotalMilliseconds, 2)
        };

        return Task.FromResult(new HealthCheckResult(status, description, null, data));
    }
}
