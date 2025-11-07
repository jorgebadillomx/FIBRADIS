using System;
using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Infrastructure.Observability.Health;

public interface ISqlServerHealthProbe
{
    Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed record HealthProbeResult(bool IsSuccessful, string? Message = null, TimeSpan? Duration = null);

public sealed class NoopSqlServerHealthProbe : ISqlServerHealthProbe
{
    public Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(new HealthProbeResult(true, "In-memory database", TimeSpan.Zero));
}
