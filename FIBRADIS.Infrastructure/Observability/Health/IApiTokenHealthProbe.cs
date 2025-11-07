using System;
using System.Threading;
using System.Threading.Tasks;

namespace FIBRADIS.Infrastructure.Observability.Health;

public interface IApiTokenHealthProbe
{
    Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class NoopApiTokenHealthProbe : IApiTokenHealthProbe
{
    public Task<HealthProbeResult> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(new HealthProbeResult(true, "Token validation not configured", TimeSpan.Zero));
}
