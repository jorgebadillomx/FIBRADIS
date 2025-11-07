using System;

namespace FIBRADIS.Infrastructure.Observability;

public interface ISystemUptimeProvider
{
    TimeSpan GetUptime();
    DateTimeOffset StartedAt { get; }
}

public sealed class SystemUptimeService : ISystemUptimeProvider
{
    private readonly DateTimeOffset _startedAt;

    public SystemUptimeService()
    {
        _startedAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAt => _startedAt;

    public TimeSpan GetUptime() => DateTimeOffset.UtcNow - _startedAt;
}
