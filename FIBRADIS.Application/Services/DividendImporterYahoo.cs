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

public sealed class DividendImporterYahoo : IDividendImporterYahoo
{
    private readonly IDistributionRepository _distributionRepository;
    private readonly IYahooFinanceClient _yahooFinanceClient;
    private readonly IDividendsMetricsCollector _metricsCollector;
    private readonly ILogger<DividendImporterYahoo> _logger;
    private readonly int _maxAttempts;

    public DividendImporterYahoo(
        IDistributionRepository distributionRepository,
        IYahooFinanceClient yahooFinanceClient,
        IDividendsMetricsCollector metricsCollector,
        ILogger<DividendImporterYahoo> logger,
        int maxAttempts = 3)
    {
        _distributionRepository = distributionRepository ?? throw new ArgumentNullException(nameof(distributionRepository));
        _yahooFinanceClient = yahooFinanceClient ?? throw new ArgumentNullException(nameof(yahooFinanceClient));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxAttempts = maxAttempts;
    }

    public async Task<DividendImportSummary> ImportAsync(CancellationToken ct)
    {
        var tickers = await _distributionRepository.GetActiveFibraTickersAsync(ct).ConfigureAwait(false);
        var totalImported = 0;
        var totalDuplicates = 0;
        var totalFailed = 0;
        var warnings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in tickers)
        {
            _metricsCollector.RecordPullAttempt(ticker);
            var attempt = 0;
            List<Services.Models.YahooDividendEvent> dividendEvents;
            var sw = Stopwatch.StartNew();

            while (true)
            {
                attempt++;
                try
                {
                    dividendEvents = (await _yahooFinanceClient
                            .GetDividendSeriesAsync(ticker, ct)
                            .ConfigureAwait(false))
                        .ToList();
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < _maxAttempts)
                {
                    _logger.LogWarning(ex, "Retrying Yahoo dividends import for {Ticker} attempt {Attempt}", ticker, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    totalFailed++;
                    _metricsCollector.RecordPullFailure(ticker);
                    _logger.LogError(ex, "Failed to import Yahoo dividends for {Ticker}", ticker);
                    goto NextTicker;
                }
            }

            if (dividendEvents.Count == 0)
            {
                warnings[ticker] = "empty";
                _metricsCollector.RecordPullWarning(ticker, "empty response");
                _logger.LogWarning("Yahoo dividends import returned empty payload for {Ticker}", ticker);
                goto NextTicker;
            }

            var imported = 0;
            var duplicates = 0;
            foreach (var @event in dividendEvents)
            {
                if (@event.GrossAmount <= 0)
                {
                    continue;
                }

                var payDate = @event.PayDate.Date;
                if (await _distributionRepository.ExistsAsync(ticker, payDate, @event.GrossAmount, ct)
                        .ConfigureAwait(false))
                {
                    duplicates++;
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var record = new DistributionRecord
                {
                    Id = Guid.NewGuid(),
                    Ticker = ticker,
                    PayDate = payDate,
                    ExDate = @event.ExDate?.Date,
                    GrossPerCbfi = Decimal.Round(@event.GrossAmount, 6, MidpointRounding.AwayFromZero),
                    Currency = @event.Currency,
                    Source = "Yahoo",
                    Confidence = 0.5m,
                    Status = "imported",
                    Type = "Dividend",
                    PeriodTag = DistributionPeriodHelper.GetPeriodTag(payDate),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                await _distributionRepository.InsertAsync(record, ct).ConfigureAwait(false);
                imported++;
            }

            sw.Stop();
            totalImported += imported;
            totalDuplicates += duplicates;
            _metricsCollector.RecordPullSuccess(ticker, imported, duplicates, sw.Elapsed);
            _logger.LogInformation(
                "Yahoo dividends import completed for {Ticker} imported={Imported} duplicates={Duplicates} duration={ElapsedMs}ms",
                ticker,
                imported,
                duplicates,
                sw.ElapsedMilliseconds);

        NextTicker:
            continue;
        }

        return new DividendImportSummary
        {
            CountImported = totalImported,
            CountDuplicates = totalDuplicates,
            CountFailed = totalFailed,
            Warnings = warnings
        };
    }
}
