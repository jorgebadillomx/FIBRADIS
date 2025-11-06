using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryDistributionReader : IDistributionReader, IDistributionMetricsWriter
{
    private readonly ConcurrentDictionary<string, (decimal? YieldTtm, decimal? YieldForward)> _yields =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<(decimal? YieldTtm, decimal? YieldForward)> GetYieldsAsync(string ticker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return Task.FromResult<(decimal?, decimal?)>((null, null));
        }

        var normalized = ticker.Trim().ToUpperInvariant();
        if (_yields.TryGetValue(normalized, out var yield))
        {
            return Task.FromResult(yield);
        }

        var generated = (YieldTtm: (decimal?)Math.Round(Random.Shared.NextDouble() * 0.08, 4),
            YieldForward: (decimal?)Math.Round(Random.Shared.NextDouble() * 0.08, 4));
        _yields[normalized] = generated;
        return Task.FromResult(generated);
    }

    public void SetYield(string ticker, decimal? yieldTtm, decimal? yieldForward)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker must be provided", nameof(ticker));
        }

        _yields[ticker.Trim().ToUpperInvariant()] = (yieldTtm, yieldForward);
    }

    public Task SetYieldsAsync(string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
    {
        SetYield(ticker, yieldTtm, yieldForward);
        return Task.CompletedTask;
    }
}
