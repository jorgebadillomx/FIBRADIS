using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hangfire;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class PortfolioRecalcJob
{
    private const string UploadReason = "upload";
    private const string QueueName = "recalc";
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ISecurityCatalog _securityCatalog;
    private readonly IDistributionReader _distributionReader;
    private readonly IClock _clock;
    private readonly IPortfolioRecalcMetricsCollector _metricsCollector;
    private readonly ILogger<PortfolioRecalcJob> _logger;

    public PortfolioRecalcJob(
        IPortfolioRepository portfolioRepository,
        ISecurityCatalog securityCatalog,
        IDistributionReader distributionReader,
        IClock clock,
        IPortfolioRecalcMetricsCollector metricsCollector,
        ILogger<PortfolioRecalcJob> logger)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _securityCatalog = securityCatalog ?? throw new ArgumentNullException(nameof(securityCatalog));
        _distributionReader = distributionReader ?? throw new ArgumentNullException(nameof(distributionReader));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 2, 4, 8, 16, 32 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(PortfolioRecalcJobInput input, CancellationToken ct = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.UserId))
        {
            throw new ArgumentException("UserId must be provided", nameof(input));
        }

        var reason = string.IsNullOrWhiteSpace(input.Reason) ? UploadReason : input.Reason.Trim();

        _metricsCollector.RecordInvocation();

        var startedAt = _clock.UtcNow;
        var jobRunId = Guid.NewGuid();
        var executionDate = DateOnly.FromDateTime(startedAt.UtcDateTime);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting PortfolioRecalcJob {JobRunId} for user {UserId} reason {Reason} requestedAt {RequestedAt:o}",
            jobRunId,
            input.UserId,
            reason,
            input.RequestedAt);

        if (!string.Equals(reason, UploadReason, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _portfolioRepository.GetJobRunAsync(input.UserId, reason, executionDate, ct).ConfigureAwait(false);
            if (existing is not null && !string.Equals(existing.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                await _portfolioRepository.SaveJobRunAsync(new PortfolioJobRunRecord
                {
                    JobRunId = jobRunId,
                    UserId = input.UserId,
                    Reason = reason,
                    ExecutionDate = executionDate,
                    StartedAt = startedAt,
                    CompletedAt = startedAt,
                    Status = "Skipped",
                    PositionsProcessed = existing.PositionsProcessed,
                    MetricsUpdated = false,
                    Duration = TimeSpan.Zero,
                    ErrorMessage = null
                }, ct).ConfigureAwait(false);

                _metricsCollector.RecordSuccess(TimeSpan.Zero, 0, null);
                stopwatch.Stop();
                _logger.LogInformation(
                    "Skipping PortfolioRecalcJob for user {UserId} reason {Reason} on {Date} due to idempotency",
                    input.UserId,
                    reason,
                    executionDate);
                return;
            }
        }

        await _portfolioRepository.SaveJobRunAsync(new PortfolioJobRunRecord
        {
            JobRunId = jobRunId,
            UserId = input.UserId,
            Reason = reason,
            ExecutionDate = executionDate,
            StartedAt = startedAt,
            Status = "Running",
            PositionsProcessed = 0,
            MetricsUpdated = false,
            Duration = null,
            ErrorMessage = null,
            CompletedAt = null
        }, ct).ConfigureAwait(false);

        try
        {
            ct.ThrowIfCancellationRequested();

            var positions = await _portfolioRepository.GetCurrentPositionsAsync(input.UserId, ct).ConfigureAwait(false);
            var tickers = positions.Select(static position => position.ticker).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var priceMap = await ResolvePricesAsync(tickers, ct).ConfigureAwait(false);
            var yieldMap = await ResolveYieldsAsync(tickers, ct).ConfigureAwait(false);

            var valuationHistory = await _portfolioRepository.GetValuationHistoryAsync(input.UserId, ct).ConfigureAwait(false);
            var cashflows = await _portfolioRepository.GetCashflowHistoryAsync(input.UserId, ct).ConfigureAwait(false);

            var snapshot = BuildMetricsSnapshot(positions, priceMap, yieldMap, valuationHistory, cashflows, startedAt);

            await _portfolioRepository.SaveCurrentMetricsAsync(input.UserId, snapshot, ct).ConfigureAwait(false);
            await _portfolioRepository.AppendMetricsHistoryAsync(input.UserId, snapshot, jobRunId, reason, ct).ConfigureAwait(false);

            stopwatch.Stop();

            var record = new PortfolioJobRunRecord
            {
                JobRunId = jobRunId,
                UserId = input.UserId,
                Reason = reason,
                ExecutionDate = executionDate,
                StartedAt = startedAt,
                CompletedAt = startedAt + stopwatch.Elapsed,
                Status = "Success",
                PositionsProcessed = positions.Count,
                MetricsUpdated = true,
                Duration = stopwatch.Elapsed,
                ErrorMessage = null
            };

            await _portfolioRepository.SaveJobRunAsync(record, ct).ConfigureAwait(false);

            var averageYield = snapshot.YieldTtm ?? snapshot.YieldForward;
            _metricsCollector.RecordSuccess(stopwatch.Elapsed, positions.Count, averageYield);

            _logger.LogInformation(
                "PortfolioRecalcJob {JobRunId} completed for {UserId} reason {Reason} in {ElapsedMs} ms (positions={Positions}, value={Value}, pnl={Pnl})",
                jobRunId,
                input.UserId,
                reason,
                stopwatch.ElapsedMilliseconds,
                positions.Count,
                snapshot.Value,
                snapshot.Pnl);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleFailureAsync(jobRunId, input.UserId, reason, executionDate, startedAt, stopwatch.Elapsed, ex, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IDictionary<string, decimal?>> ResolvePricesAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        try
        {
            return await _securityCatalog.GetLastPricesAsync(tickers, ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            var map = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var ticker in tickers)
            {
                ct.ThrowIfCancellationRequested();
                var price = await _securityCatalog.GetLastPriceAsync(ticker, ct).ConfigureAwait(false);
                map[ticker] = price;
            }

            return map;
        }
    }

    private async Task<IDictionary<string, (decimal? YieldTtm, decimal? YieldForward)>> ResolveYieldsAsync(IEnumerable<string> tickers, CancellationToken ct)
    {
        var map = new Dictionary<string, (decimal? YieldTtm, decimal? YieldForward)>(StringComparer.OrdinalIgnoreCase);
        foreach (var ticker in tickers)
        {
            ct.ThrowIfCancellationRequested();
            map[ticker] = await _distributionReader.GetYieldsAsync(ticker, ct).ConfigureAwait(false);
        }

        return map;
    }

    private PortfolioRecalcMetricsSnapshot BuildMetricsSnapshot(
        IReadOnlyList<(string ticker, decimal qty, decimal avgCost)> positions,
        IDictionary<string, decimal?> priceMap,
        IDictionary<string, (decimal? YieldTtm, decimal? YieldForward)> yieldMap,
        IReadOnlyList<PortfolioValuationSnapshot> valuations,
        IReadOnlyList<PortfolioCashflow> cashflows,
        DateTimeOffset calculatedAt)
    {
        var invested = 0m;
        var value = 0m;
        var weightedYieldTtm = 0m;
        var weightedYieldForward = 0m;
        var totalWeightForYields = 0m;

        foreach (var (ticker, qty, avgCost) in positions)
        {
            invested += qty * avgCost;

            var price = priceMap.TryGetValue(ticker, out var candidate) ? candidate : null;
            var positionValue = price.HasValue ? qty * price.Value : 0m;
            value += positionValue;

            if (yieldMap.TryGetValue(ticker, out var yields))
            {
                var weight = positionValue > 0 ? positionValue : qty * avgCost;
                if (yields.YieldTtm.HasValue)
                {
                    weightedYieldTtm += yields.YieldTtm.Value * weight;
                }

                if (yields.YieldForward.HasValue)
                {
                    weightedYieldForward += yields.YieldForward.Value * weight;
                }

                totalWeightForYields += weight;
            }
        }

        var pnl = value - invested;
        decimal? yieldTtm = null;
        decimal? yieldForward = null;

        if (totalWeightForYields > 0)
        {
            if (weightedYieldTtm > 0)
            {
                yieldTtm = Math.Round(weightedYieldTtm / totalWeightForYields, 6, MidpointRounding.AwayFromZero);
            }

            if (weightedYieldForward > 0)
            {
                yieldForward = Math.Round(weightedYieldForward / totalWeightForYields, 6, MidpointRounding.AwayFromZero);
            }
        }

        var twr = ComputeTimeWeightedReturn(valuations, cashflows);
        var mwr = ComputeMoneyWeightedReturn(valuations, cashflows);
        var span = GetTotalSpan(valuations);

        var annualizedTwr = twr.HasValue && span.TotalDays > 0
            ? (decimal)Math.Pow((double)(1m + twr.Value), 365d / span.TotalDays) - 1m
            : (decimal?)null;

        var annualizedMwr = mwr.HasValue && span.TotalDays > 0
            ? (decimal)Math.Pow((double)(1m + mwr.Value), 365d / span.TotalDays) - 1m
            : (decimal?)null;

        return new PortfolioRecalcMetricsSnapshot
        {
            Invested = Math.Round(invested, 2, MidpointRounding.AwayFromZero),
            Value = Math.Round(value, 2, MidpointRounding.AwayFromZero),
            Pnl = Math.Round(pnl, 2, MidpointRounding.AwayFromZero),
            YieldTtm = yieldTtm,
            YieldForward = yieldForward,
            TimeWeightedReturn = twr,
            MoneyWeightedReturn = mwr,
            AnnualizedTimeWeightedReturn = annualizedTwr,
            AnnualizedMoneyWeightedReturn = annualizedMwr,
            CalculatedAt = calculatedAt
        };
    }

    private static TimeSpan GetTotalSpan(IReadOnlyList<PortfolioValuationSnapshot> valuations)
    {
        if (valuations.Count < 2)
        {
            return TimeSpan.Zero;
        }

        var ordered = valuations.OrderBy(snapshot => snapshot.AsOf).ToList();
        return ordered[^1].AsOf - ordered[0].AsOf;
    }

    private static decimal? ComputeTimeWeightedReturn(
        IReadOnlyList<PortfolioValuationSnapshot> valuations,
        IReadOnlyList<PortfolioCashflow> cashflows)
    {
        if (valuations.Count < 2)
        {
            return null;
        }

        var orderedValuations = valuations.OrderBy(snapshot => snapshot.AsOf).ToList();
        var orderedFlows = cashflows.OrderBy(flow => flow.Timestamp).ToList();

        decimal cumulative = 1m;
        var previousValuation = orderedValuations[0];

        for (var i = 1; i < orderedValuations.Count; i++)
        {
            var current = orderedValuations[i];
            var periodFlows = orderedFlows
                .Where(flow => flow.Timestamp > previousValuation.AsOf && flow.Timestamp <= current.AsOf)
                .Sum(flow => flow.Amount);

            if (previousValuation.Value <= 0)
            {
                previousValuation = current;
                continue;
            }

            var periodReturn = (current.Value - periodFlows - previousValuation.Value) / previousValuation.Value;
            cumulative *= 1m + periodReturn;
            previousValuation = current;
        }

        return cumulative - 1m;
    }

    private static decimal? ComputeMoneyWeightedReturn(
        IReadOnlyList<PortfolioValuationSnapshot> valuations,
        IReadOnlyList<PortfolioCashflow> cashflows)
    {
        if (valuations.Count == 0)
        {
            return null;
        }

        var orderedValuations = valuations.OrderBy(snapshot => snapshot.AsOf).ToList();
        var flows = cashflows
            .Select(flow => (flow.Timestamp, Amount: -flow.Amount))
            .Concat(new[] { (orderedValuations[^1].AsOf, orderedValuations[^1].Value) })
            .OrderBy(tuple => tuple.Timestamp)
            .ToList();

        if (flows.Count < 2)
        {
            return null;
        }

        double IrrFunction(double rate)
        {
            double sum = 0d;
            var baseDate = flows[0].Timestamp;
            foreach (var (timestamp, amount) in flows)
            {
                var days = (timestamp - baseDate).TotalDays;
                sum += (double)amount / Math.Pow(1d + rate, days / 365d);
            }

            return sum;
        }

        double Derivative(double rate)
        {
            double sum = 0d;
            var baseDate = flows[0].Timestamp;
            foreach (var (timestamp, amount) in flows)
            {
                var days = (timestamp - baseDate).TotalDays;
                var exponent = days / 365d;
                sum += -(double)amount * exponent / Math.Pow(1d + rate, exponent + 1d);
            }

            return sum;
        }

        const double Tolerance = 1e-7;
        const int MaxIterations = 50;
        double guess = 0.1d;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var value = IrrFunction(guess);
            if (Math.Abs(value) < Tolerance)
            {
                return (decimal)guess;
            }

            var derivative = Derivative(guess);
            if (Math.Abs(derivative) < double.Epsilon)
            {
                break;
            }

            guess -= value / derivative;
        }

        return null;
    }

    private async Task HandleFailureAsync(
        Guid jobRunId,
        string userId,
        string reason,
        DateOnly executionDate,
        DateTimeOffset startedAt,
        TimeSpan elapsed,
        Exception exception,
        CancellationToken ct)
    {
        _metricsCollector.RecordFailure(elapsed);
        _logger.LogError(exception, "PortfolioRecalcJob {JobRunId} failed for {UserId} reason {Reason}", jobRunId, userId, reason);

        var failureRecord = new PortfolioJobRunRecord
        {
            JobRunId = jobRunId,
            UserId = userId,
            Reason = reason,
            ExecutionDate = executionDate,
            StartedAt = startedAt,
            CompletedAt = startedAt + elapsed,
            Status = "Failed",
            PositionsProcessed = 0,
            MetricsUpdated = false,
            Duration = elapsed,
            ErrorMessage = exception.Message
        };

        try
        {
            await _portfolioRepository.SaveJobRunAsync(failureRecord, ct).ConfigureAwait(false);
            await _portfolioRepository.RecordDeadLetterAsync(new PortfolioJobDeadLetterRecord
            {
                JobRunId = jobRunId,
                UserId = userId,
                Reason = reason,
                FailedAt = startedAt + elapsed,
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                Message = exception.Message,
                StackTrace = exception.StackTrace
            }, ct).ConfigureAwait(false);
        }
        catch (Exception persistenceException)
        {
            _logger.LogError(
                persistenceException,
                "Failed to persist failure audit for PortfolioRecalcJob {JobRunId} (original error: {Message})",
                jobRunId,
                exception.Message);
        }
    }
}

public sealed record PortfolioRecalcJobInput
{
    public required string UserId { get; init; }

    public string Reason { get; init; } = "upload";

    public DateTimeOffset RequestedAt { get; init; }
}
