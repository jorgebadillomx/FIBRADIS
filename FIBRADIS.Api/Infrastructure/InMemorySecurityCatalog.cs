using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySecurityCatalog : ISecurityCatalog
{
    private static readonly string[] DefaultTickers =
    {
        "FUNO11", "FIBRATC14", "TERRA13", "MQCC11", "FFAU11", "FSHF11"
    };

    private readonly ConcurrentDictionary<string, decimal> _prices;

    public InMemorySecurityCatalog()
    {
        _prices = new ConcurrentDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var random = new Random(Seed: 17);
        foreach (var ticker in DefaultTickers)
        {
            _prices[ticker] = Math.Round((decimal)(random.NextDouble() * 50 + 15), 2);
        }
    }

    public Task<decimal?> GetLastPriceAsync(string ticker, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return Task.FromResult<decimal?>(null);
        }

        var normalized = ticker.Trim().ToUpperInvariant();
        return Task.FromResult(_prices.TryGetValue(normalized, out var price) ? price : (decimal?)null);
    }

    public override Task<IDictionary<string, decimal?>> GetLastPricesAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var map = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in tickers)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                continue;
            }

            var normalized = ticker.Trim().ToUpperInvariant();
            map[normalized] = _prices.TryGetValue(normalized, out var price) ? price : (decimal?)null;
        }

        return Task.FromResult<IDictionary<string, decimal?>>(map);
    }

    public void SetPrice(string ticker, decimal price)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new ArgumentException("Ticker must be provided", nameof(ticker));
        }

        _prices[ticker.Trim().ToUpperInvariant()] = price;
    }
}
