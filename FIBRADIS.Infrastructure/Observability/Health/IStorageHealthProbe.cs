using System;
using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Infrastructure.Observability.Health;

public interface IStorageHealthProbe
{
    Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class NoopStorageHealthProbe : IStorageHealthProbe
{
    public Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(new HealthProbeResult(true, "In-memory storage", TimeSpan.Zero));
}
