using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class PermissiveRobotsPolicy : IRobotsPolicy
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    public Task<bool> IsAllowedAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return Task.FromResult(true);
    }

    public Task<TimeSpan> GetCrawlDelayAsync(string domain, CancellationToken ct)
    {
        return Task.FromResult(DefaultDelay);
    }
}
