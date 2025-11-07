using System;
using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Infrastructure.Observability.Health;

public interface IBackgroundJobHealthProbe
{
    Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class NoopBackgroundJobHealthProbe : IBackgroundJobHealthProbe
{
    public Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(new HealthProbeResult(true, "In-memory scheduler", TimeSpan.Zero));
}
