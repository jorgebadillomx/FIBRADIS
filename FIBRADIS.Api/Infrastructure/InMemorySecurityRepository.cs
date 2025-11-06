using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySecurityRepository : ISecurityRepository
{
    private readonly ConcurrentDictionary<string, (decimal? YieldTtm, decimal? YieldForward)> _yields =
        new(StringComparer.OrdinalIgnoreCase);

    public Task UpdateYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker must be provided", nameof(ticker));
        }

        _yields[ticker.Trim().ToUpperInvariant()] = (yieldTtm, yieldForward);
        return Task.CompletedTask;
    }

    public (decimal? YieldTtm, decimal? YieldForward)? GetYields(string ticker)
    {
        return _yields.TryGetValue(ticker, out var value) ? value : null;
    }
}
