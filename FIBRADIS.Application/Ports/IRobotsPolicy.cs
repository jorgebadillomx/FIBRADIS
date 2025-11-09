namespace FIBRADIS.Application.Ports;

public interface IRobotsPolicy
{
    Task<bool> IsAllowedAsync(Uri uri, CancellationToken ct);
    Task<TimeSpan> GetCrawlDelayAsync(string domain, CancellationToken ct);
}
