using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class PortfolioReplaceService : IPortfolioReplaceService
{
    private const string UploadReason = "upload";
    private const int DecimalPrecision = 6;

    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ISecurityCatalog _securityCatalog;
    private readonly IDistributionReader _distributionReader;
    private readonly IJobScheduler _jobScheduler;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlationIdAccessor;
    private readonly ILogger<PortfolioReplaceService> _logger;

    public PortfolioReplaceService(
        IPortfolioRepository portfolioRepository,
        ISecurityCatalog securityCatalog,
        IDistributionReader distributionReader,
        IJobScheduler jobScheduler,
        IClock clock,
        ICorrelationIdAccessor correlationIdAccessor,
        ILogger<PortfolioReplaceService> logger)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _securityCatalog = securityCatalog ?? throw new ArgumentNullException(nameof(securityCatalog));
        _distributionReader = distributionReader ?? throw new ArgumentNullException(nameof(distributionReader));
        _jobScheduler = jobScheduler ?? throw new ArgumentNullException(nameof(jobScheduler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _correlationIdAccessor = correlationIdAccessor ?? throw new ArgumentNullException(nameof(correlationIdAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<UploadPortfolioResponse> ReplaceAsync(string userId, IEnumerable<NormalizedRow> rows, IEnumerable<ValidationIssue> issuesFromParser, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(rows);

        ct.ThrowIfCancellationRequested();

        var startedAt = _clock.UtcNow;
        var requestId = ResolveRequestId();
        var stopwatch = Stopwatch.StartNew();

        var parserIssues = (issuesFromParser ?? Enumerable.Empty<ValidationIssue>()).ToList();
        var ignoredFromParser = parserIssues.Count(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));

        var consolidatedRows = ConsolidateRows(rows, ct, out var ignoredByService, out var duplicateCount);

        await _portfolioRepository.BeginTransactionAsync(ct);
        List<(string ticker, decimal qty, decimal avgCost)> materializedPositions;
        try
        {
            await _portfolioRepository.DeleteUserPortfolioAsync(userId, ct);
            await _portfolioRepository.InsertTradesAsync(userId, consolidatedRows.Select(row => (row.Ticker, row.Qty, row.AvgCost)), ct);
            materializedPositions = await _portfolioRepository.GetMaterializedPositionsAsync(userId, ct);
            await _portfolioRepository.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(ct);
            stopwatch.Stop();
            _logger.LogError(ex, "Portfolio replace failed for {UserId} (requestId: {RequestId})", userId, requestId);
            throw;
        }

        ct.ThrowIfCancellationRequested();

        var priceMap = await ResolvePricesAsync(materializedPositions.Select(position => position.ticker), ct);
        var yieldMap = await ResolveYieldsAsync(materializedPositions.Select(position => position.ticker), ct);

        var snapshot = BuildSnapshot(materializedPositions, priceMap, yieldMap);

        _jobScheduler.EnqueuePortfolioRecalc(userId, UploadReason, startedAt);

        stopwatch.Stop();

        var totalIgnored = ignoredFromParser + ignoredByService;
        var response = new UploadPortfolioResponse
        {
            Imported = snapshot.Positions.Count,
            Ignored = totalIgnored,
            Errors = 0,
            Positions = snapshot.Positions,
            Metrics = snapshot.Metrics,
            RequestId = requestId
        };

        _logger.LogInformation(
            "Portfolio replace completed for {UserId} (requestId: {RequestId}) in {ElapsedMs} ms: imported={Imported}, ignored={Ignored}, positions={Positions}, duplicates={Duplicates}",
            userId,
            requestId,
            stopwatch.ElapsedMilliseconds,
            response.Imported,
            response.Ignored,
            response.Positions.Count,
            duplicateCount);

        _logger.LogDebug(
            "portfolio.upload.replace audit: user={UserId}, positions={Positions}, imported={Imported}, ignored={Ignored}, startedAt={StartedAt:o}, elapsedMs={ElapsedMs}",
            userId,
            response.Positions.Count,
            response.Imported,
            response.Ignored,
            startedAt,
            stopwatch.ElapsedMilliseconds);

        return response;
    }

    private IReadOnlyList<NormalizedRow> ConsolidateRows(IEnumerable<NormalizedRow> rows, CancellationToken ct, out int ignored, out int duplicates)
    {
        ignored = 0;
        duplicates = 0;
        var aggregates = new Dictionary<string, (decimal qty, decimal invested)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (row is null)
            {
                continue;
            }

            var ticker = row.Ticker?.Trim();
            if (string.IsNullOrEmpty(ticker))
            {
                ignored++;
                continue;
            }

            ticker = ticker.ToUpperInvariant();

            if (row.Qty <= 0 || row.AvgCost <= 0)
            {
                ignored++;
                continue;
            }

            if (aggregates.TryGetValue(ticker, out var current))
            {
                duplicates++;
                var updatedQty = current.qty + row.Qty;
                var updatedInvested = current.invested + (row.Qty * row.AvgCost);
                aggregates[ticker] = (updatedQty, updatedInvested);
            }
            else
            {
                aggregates[ticker] = (row.Qty, row.Qty * row.AvgCost);
            }
        }

        return aggregates
            .Select(pair => new NormalizedRow
            {
                Ticker = pair.Key,
                Qty = Round(pair.Value.qty),
                AvgCost = pair.Value.qty == 0 ? 0 : Round(pair.Value.invested / pair.Value.qty)
            })
            .ToList();
    }

    private async Task<IDictionary<string, decimal?>> ResolvePricesAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        var distinctTickers = tickers
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(ticker => ticker.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctTickers.Length == 0)
        {
            return new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var batch = await _securityCatalog.GetLastPricesAsync(distinctTickers, ct);
            if (batch is not null)
            {
                return new Dictionary<string, decimal?>(batch, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (NotSupportedException)
        {
            // Fall back to single fetches when batch retrieval is not available.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve batch prices for tickers {Tickers}", distinctTickers);
            return distinctTickers.ToDictionary(ticker => ticker, _ => (decimal?)null, StringComparer.OrdinalIgnoreCase);
        }

        var prices = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in distinctTickers)
        {
            try
            {
                prices[ticker] = await _securityCatalog.GetLastPriceAsync(ticker, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve price for ticker {Ticker}", ticker);
                prices[ticker] = null;
            }
        }

        return prices;
    }

    private async Task<IDictionary<string, (decimal? YieldTtm, decimal? YieldForward)>> ResolveYieldsAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        var distinctTickers = tickers
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(ticker => ticker.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var yields = new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in distinctTickers)
        {
            try
            {
                yields[ticker] = await _distributionReader.GetYieldsAsync(ticker, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve yields for ticker {Ticker}", ticker);
                yields[ticker] = (null, null);
            }
        }

        return yields;
    }

    private (IReadOnlyList<PositionSnapshotDto> Positions, PortfolioMetricsDto Metrics) BuildSnapshot(
        IEnumerable<(string ticker, decimal qty, decimal avgCost)> materialized,
        IDictionary<string, decimal?> priceMap,
        IDictionary<string, (decimal? YieldTtm, decimal? YieldForward)> yieldMap)
    {
        var positions = new List<PositionSnapshotDto>();
        var totalInvested = 0m;
        var totalValue = 0m;
        var yieldTtmNumerator = 0m;
        var yieldTtmDenominator = 0m;
        var yieldForwardNumerator = 0m;
        var yieldForwardDenominator = 0m;

        foreach (var (tickerRaw, qty, avgCost) in materialized)
        {
            var ticker = (tickerRaw ?? string.Empty).Trim().ToUpperInvariant();
            var sanitizedQty = Round(qty);
            var sanitizedAvgCost = Round(avgCost);
            var invested = Round(sanitizedQty * sanitizedAvgCost);

            priceMap.TryGetValue(ticker, out var price);
            decimal? marketPrice = price.HasValue ? Round(price.Value) : null;
            var value = marketPrice.HasValue ? Round(marketPrice.Value * sanitizedQty) : 0m;
            var pnl = Round(value - invested);

            yieldMap.TryGetValue(ticker, out var yieldInfo);
            decimal? yieldTtm = yieldInfo.YieldTtm.HasValue ? Round(yieldInfo.YieldTtm.Value) : null;
            decimal? yieldForward = yieldInfo.YieldForward.HasValue ? Round(yieldInfo.YieldForward.Value) : null;

            totalInvested += invested;
            totalValue += value;

            if (yieldTtm.HasValue && value > 0)
            {
                yieldTtmNumerator += value * yieldTtm.Value;
                yieldTtmDenominator += value;
            }

            if (yieldForward.HasValue && value > 0)
            {
                yieldForwardNumerator += value * yieldForward.Value;
                yieldForwardDenominator += value;
            }

            positions.Add(new PositionSnapshotDto
            {
                Ticker = ticker,
                Qty = sanitizedQty,
                AvgCost = sanitizedAvgCost,
                Invested = invested,
                MarketPrice = marketPrice,
                Value = value,
                Pnl = pnl,
                Weight = 0m,
                YieldTtm = yieldTtm,
                YieldForward = yieldForward
            });
        }

        var ordered = positions
            .OrderByDescending(position => position.Value)
            .ThenBy(position => position.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(position =>
            {
                var weight = totalValue > 0 ? Round(position.Value / totalValue) : 0m;
                return position with { Weight = weight };
            })
            .ToList();

        var metrics = new PortfolioMetricsDto
        {
            Invested = Round(totalInvested),
            Value = Round(totalValue),
            Pnl = Round(totalValue - totalInvested),
            YieldTtm = yieldTtmDenominator > 0 ? Round(yieldTtmNumerator / yieldTtmDenominator) : null,
            YieldForward = yieldForwardDenominator > 0 ? Round(yieldForwardNumerator / yieldForwardDenominator) : null
        };

        return (ordered, metrics);
    }

    private async Task SafeRollbackAsync(CancellationToken ct)
    {
        try
        {
            await _portfolioRepository.RollbackAsync(ct);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "Failed to rollback transaction during portfolio replace");
        }
    }

    private string ResolveRequestId()
    {
        var correlationId = _correlationIdAccessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static decimal Round(decimal value) => Math.Round(value, DecimalPrecision, MidpointRounding.AwayFromZero);
}
