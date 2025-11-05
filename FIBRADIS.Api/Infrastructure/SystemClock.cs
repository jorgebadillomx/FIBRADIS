using FIBRADIS.Application.Abstractions;

namespace FIBRADIS.Api.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
