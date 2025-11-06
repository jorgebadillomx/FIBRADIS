using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class DistributionReconcilerService : IDistributionReconcilerService
{
    private readonly IDistributionRepository _distributionRepository;
    private readonly IOfficialDistributionSource _officialDistributionSource;
    private readonly ISecurityCatalog _securityCatalog;
    private readonly ISecurityRepository _securityRepository;
    private readonly IDistributionMetricsWriter _distributionMetricsWriter;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IJobScheduler _jobScheduler;
    private readonly IDistributionReconcileMetricsCollector _metricsCollector;
    private readonly IClock _clock;
    private readonly ILogger<DistributionReconcilerService> _logger;

    public DistributionReconcilerService(
        IDistributionRepository distributionRepository,
        IOfficialDistributionSource officialDistributionSource,
        ISecurityCatalog securityCatalog,
        ISecurityRepository securityRepository,
        IDistributionMetricsWriter distributionMetricsWriter,
        IPortfolioRepository portfolioRepository,
        IJobScheduler jobScheduler,
        IDistributionReconcileMetricsCollector metricsCollector,
        IClock clock,
        ILogger<DistributionReconcilerService> logger)
    {
        _distributionRepository = distributionRepository ?? throw new ArgumentNullException(nameof(distributionRepository));
        _officialDistributionSource = officialDistributionSource ?? throw new ArgumentNullException(nameof(officialDistributionSource));
        _securityCatalog = securityCatalog ?? throw new ArgumentNullException(nameof(securityCatalog));
        _securityRepository = securityRepository ?? throw new ArgumentNullException(nameof(securityRepository));
        _distributionMetricsWriter = distributionMetricsWriter ?? throw new ArgumentNullException(nameof(distributionMetricsWriter));
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _jobScheduler = jobScheduler ?? throw new ArgumentNullException(nameof(jobScheduler));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DistributionReconcileSummary> ReconcileAsync(CancellationToken ct)
    {
        var imported = await _distributionRepository.GetByStatusAsync("imported", ct).ConfigureAwait(false);
        var verified = 0;
        var ignored = 0;
        var splitCount = 0;
        var affectedTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var affectedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in imported.GroupBy(record => record.Ticker, StringComparer.OrdinalIgnoreCase))
        {
            var ticker = group.Key;
            _metricsCollector.RecordReconcileAttempt(ticker);
            var sw = Stopwatch.StartNew();
            try
            {
                var updated = await ReconcileTickerAsync(ticker, group.ToList(), affectedTickers, affectedUsers, ct).ConfigureAwait(false);
                verified += updated.Verified;
                ignored += updated.Ignored;
                splitCount += updated.Splits;
                sw.Stop();
                _metricsCollector.RecordReconcileResult(ticker, updated.Verified, updated.Ignored, updated.Splits, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _metricsCollector.RecordReconcileFailure(ticker);
                _logger.LogError(ex, "Failed to reconcile distributions for {Ticker}", ticker);
            }
        }

        foreach (var ticker in affectedTickers)
        {
            await ComputeYieldsAsync(ticker, ct).ConfigureAwait(false);
        }

        var now = _clock.UtcNow;
        foreach (var userId in affectedUsers)
        {
            _jobScheduler.EnqueuePortfolioRecalc(userId, "distribution", now);
        }

        return new DistributionReconcileSummary
        {
            ImportedCount = imported.Count,
            VerifiedCount = verified,
            IgnoredCount = ignored,
            SplitCount = splitCount
        };
    }

    private async Task<(int Verified, int Ignored, int Splits)> ReconcileTickerAsync(
        string ticker,
        IReadOnlyList<DistributionRecord> imported,
        HashSet<string> affectedTickers,
        HashSet<string> affectedUsers,
        CancellationToken ct)
    {
        var payDate = imported.Min(record => record.PayDate);
        var official = await _officialDistributionSource
            .GetOfficialDistributionsAsync(ticker, payDate, ct)
            .ConfigureAwait(false);

        var verified = 0;
        var ignored = 0;
        var splits = 0;

        foreach (var record in imported)
        {
            var match = FindBestMatch(record, official);
            if (match is not null)
            {
                var outcome = await ApplyMatchAsync(record, match.Value, affectedTickers, affectedUsers, ct).ConfigureAwait(false);
                verified += outcome.Verified;
                ignored += outcome.Ignored;
                continue;
            }

            var multiMatch = TrySplit(record, official);
            if (multiMatch.Count > 0)
            {
                var applied = await ApplySplitAsync(record, multiMatch, affectedTickers, affectedUsers, ct).ConfigureAwait(false);
                splits += applied;
                verified += applied;
                continue;
            }

            _logger.LogWarning(
                "Could not reconcile distribution for {Ticker} payDate={PayDate:yyyy-MM-dd} gross={Gross}",
                ticker,
                record.PayDate,
                record.GrossPerCbfi);
        }

        return (verified, ignored, splits);
    }

    private (OfficialDistributionRecord Record, decimal Difference)? FindBestMatch(DistributionRecord imported, IReadOnlyList<OfficialDistributionRecord> official)
    {
        var thresholdDate = (Min: imported.PayDate.AddDays(-7), Max: imported.PayDate.AddDays(7));
        var matches = official
            .Where(candidate => candidate.PayDate >= thresholdDate.Min && candidate.PayDate <= thresholdDate.Max)
            .Select(candidate => (candidate, Difference: Math.Abs(candidate.GrossPerCbfi - imported.GrossPerCbfi)))
            .OrderBy(tuple => tuple.Difference)
            .ToList();

        foreach (var (candidate, difference) in matches)
        {
            if (difference <= imported.GrossPerCbfi * 0.03m || imported.GrossPerCbfi == 0)
            {
                return (candidate, difference);
            }
        }

        return null;
    }

    private List<OfficialDistributionRecord> TrySplit(DistributionRecord imported, IReadOnlyList<OfficialDistributionRecord> official)
    {
        var thresholdDate = (Min: imported.PayDate.AddDays(-7), Max: imported.PayDate.AddDays(7));
        var candidates = official
            .Where(candidate => candidate.PayDate >= thresholdDate.Min && candidate.PayDate <= thresholdDate.Max)
            .GroupBy(candidate => candidate.PayDate)
            .SelectMany(group => group.OrderByDescending(candidate => candidate.GrossPerCbfi))
            .ToList();

        if (candidates.Count < 2)
        {
            return new List<OfficialDistributionRecord>();
        }

        var sum = candidates.Sum(candidate => candidate.GrossPerCbfi);
        if (sum <= 0)
        {
            return new List<OfficialDistributionRecord>();
        }

        var tolerance = imported.GrossPerCbfi * 0.03m;
        if (Math.Abs(sum - imported.GrossPerCbfi) <= tolerance)
        {
            return candidates;
        }

        return new List<OfficialDistributionRecord>();
    }

    private async Task<(int Verified, int Ignored)> ApplyMatchAsync(
        DistributionRecord record,
        (OfficialDistributionRecord Record, decimal Difference) match,
        HashSet<string> affectedTickers,
        HashSet<string> affectedUsers,
        CancellationToken ct)
    {
        if (match.Record.GrossPerCbfi <= 0)
        {
            record.Status = "ignored";
            record.UpdatedAt = _clock.UtcNow;
            await _distributionRepository.UpdateAsync(record, ct).ConfigureAwait(false);
            return (0, 1);
        }

        record.PayDate = match.Record.PayDate.Date;
        record.ExDate = match.Record.ExDate?.Date;
        record.GrossPerCbfi = Decimal.Round(match.Record.GrossPerCbfi, 6, MidpointRounding.AwayFromZero);
        record.Currency = match.Record.Currency;
        record.Source = match.Record.Source;
        record.Type = NormalizeType(match.Record.Type);
        record.Confidence = 0.9m;
        record.Status = "verified";
        record.PeriodTag = match.Record.PeriodTag ?? DistributionPeriodHelper.GetPeriodTag(record.PayDate);
        record.UpdatedAt = _clock.UtcNow;

        await _distributionRepository.UpdateAsync(record, ct).ConfigureAwait(false);
        affectedTickers.Add(record.Ticker);
        await RegisterImpactedUsersAsync(record.Ticker, affectedUsers, ct).ConfigureAwait(false);
        return (1, 0);
    }

    private async Task<int> ApplySplitAsync(
        DistributionRecord record,
        IReadOnlyList<OfficialDistributionRecord> officialMatches,
        HashSet<string> affectedTickers,
        HashSet<string> affectedUsers,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var first = officialMatches[0];
        record.PayDate = first.PayDate.Date;
        record.ExDate = first.ExDate?.Date;
        record.GrossPerCbfi = Decimal.Round(first.GrossPerCbfi, 6, MidpointRounding.AwayFromZero);
        record.Currency = first.Currency;
        record.Source = first.Source;
        record.Type = NormalizeType(first.Type);
        record.Confidence = 0.9m;
        record.Status = "verified";
        record.PeriodTag = first.PeriodTag ?? DistributionPeriodHelper.GetPeriodTag(record.PayDate);
        record.UpdatedAt = now;
        await _distributionRepository.UpdateAsync(record, ct).ConfigureAwait(false);

        for (var i = 1; i < officialMatches.Count; i++)
        {
            var match = officialMatches[i];
            var clone = record.Clone();
            clone.Id = Guid.NewGuid();
            clone.PayDate = match.PayDate.Date;
            clone.ExDate = match.ExDate?.Date;
            clone.GrossPerCbfi = Decimal.Round(match.GrossPerCbfi, 6, MidpointRounding.AwayFromZero);
            clone.Type = NormalizeType(match.Type);
            clone.Source = match.Source;
            clone.PeriodTag = match.PeriodTag ?? DistributionPeriodHelper.GetPeriodTag(clone.PayDate);
            clone.CreatedAt = now;
            clone.UpdatedAt = now;
            await _distributionRepository.InsertAsync(clone, ct).ConfigureAwait(false);
        }

        affectedTickers.Add(record.Ticker);
        await RegisterImpactedUsersAsync(record.Ticker, affectedUsers, ct).ConfigureAwait(false);
        return officialMatches.Count;
    }

    private async Task RegisterImpactedUsersAsync(string ticker, HashSet<string> affectedUsers, CancellationToken ct)
    {
        var users = await _portfolioRepository.GetUsersHoldingTickerAsync(ticker, ct).ConfigureAwait(false);
        foreach (var user in users)
        {
            affectedUsers.Add(user);
        }
    }

    private string NormalizeType(string type)
    {
        if (string.Equals(type, "Dividend", StringComparison.OrdinalIgnoreCase))
        {
            return "Dividend";
        }

        if (string.Equals(type, "CapitalReturn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "Capital Return", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "ReturnOfCapital", StringComparison.OrdinalIgnoreCase))
        {
            return "CapitalReturn";
        }

        return string.IsNullOrWhiteSpace(type) ? "Other" : type;
    }

    private async Task ComputeYieldsAsync(string ticker, CancellationToken ct)
    {
        var now = _clock.UtcNow.Date;
        var oneYearAgo = now.AddYears(-1);
        var distributions = await _distributionRepository
            .GetVerifiedSinceAsync(ticker, oneYearAgo, ct)
            .ConfigureAwait(false);

        if (distributions.Count == 0)
        {
            await _distributionMetricsWriter.SetYieldsAsync(ticker, null, null, ct).ConfigureAwait(false);
            await _securityRepository.UpdateYieldsAsync(ticker, null, null, ct).ConfigureAwait(false);
            _metricsCollector.RecordYieldComputed(ticker, null, null);
            return;
        }

        decimal? yieldTtm = null;
        decimal? yieldForward = null;

        var price = await _securityCatalog.GetLastPriceAsync(ticker, ct).ConfigureAwait(false);
        if (price.HasValue && price.Value > 0)
        {
            var dividendSum = distributions
                .Where(record => string.Equals(record.Type, "Dividend", StringComparison.OrdinalIgnoreCase))
                .Sum(record => record.GrossPerCbfi);
            if (dividendSum > 0)
            {
                yieldTtm = Math.Round(dividendSum / price.Value, 6, MidpointRounding.AwayFromZero);
            }

            var lastDividend = distributions
                .Where(record => string.Equals(record.Type, "Dividend", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.PayDate)
                .FirstOrDefault();
            if (lastDividend is not null && lastDividend.GrossPerCbfi > 0)
            {
                yieldForward = Math.Round((lastDividend.GrossPerCbfi * 4) / price.Value, 6, MidpointRounding.AwayFromZero);
            }
        }

        await _distributionMetricsWriter.SetYieldsAsync(ticker, yieldTtm, yieldForward, ct).ConfigureAwait(false);
        await _securityRepository.UpdateYieldsAsync(ticker, yieldTtm, yieldForward, ct).ConfigureAwait(false);
        _metricsCollector.RecordYieldComputed(ticker, yieldTtm, yieldForward);

        var users = await _portfolioRepository.GetUsersHoldingTickerAsync(ticker, ct).ConfigureAwait(false);
        foreach (var user in users)
        {
            await _portfolioRepository.UpdatePortfolioYieldMetricsAsync(user, ticker, yieldTtm, yieldForward, ct)
                .ConfigureAwait(false);
        }
    }
}
