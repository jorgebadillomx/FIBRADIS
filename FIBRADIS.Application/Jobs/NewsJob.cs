using System;
using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class NewsJob
{
    public const string QueueName = "news";

    private readonly INewsIngestService _newsIngestService;
    private readonly INewsMetricsCollector _metrics;
    private readonly IClock _clock;
    private readonly ILogger<NewsJob> _logger;

    public NewsJob(
        INewsIngestService newsIngestService,
        INewsMetricsCollector metrics,
        IClock clock,
        ILogger<NewsJob> logger)
    {
        _newsIngestService = newsIngestService ?? throw new ArgumentNullException(nameof(newsIngestService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 10, 60, 120 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(NewsJobRequest request, CancellationToken cancellationToken = default)
    {
        _metrics.RecordInvocation();
        var stopwatch = Stopwatch.StartNew();
        var since = request?.Since ?? _clock.UtcNow.AddHours(-48);

        try
        {
            var result = await _newsIngestService.IngestAsync(since, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            _metrics.RecordIngestion(stopwatch.Elapsed, result.Downloaded, result.Duplicates, result.TokensUsed, result.CostUsd);
            _logger.LogInformation(
                "News job completed: downloaded={Downloaded} stored={Stored} duplicates={Duplicates}",
                result.Downloaded,
                result.PendingNews.Count,
                result.Duplicates);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            _logger.LogError(ex, "News job failed");
            throw;
        }
    }
}
