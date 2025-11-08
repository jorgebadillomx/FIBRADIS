using System;
using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Exceptions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class SummarizeJob
{
    public const string QueueName = "summarize";
    public const string DeadLetterQueueName = "summarize_dlq";

    private readonly ISummarizeService _summarizeService;
    private readonly ISummarizeMetricsCollector _metrics;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<SummarizeJob> _logger;

    public SummarizeJob(
        ISummarizeService summarizeService,
        ISummarizeMetricsCollector metrics,
        IJobScheduler jobScheduler,
        ILogger<SummarizeJob> logger)
    {
        _summarizeService = summarizeService ?? throw new ArgumentNullException(nameof(summarizeService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _jobScheduler = jobScheduler ?? throw new ArgumentNullException(nameof(jobScheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 10, 30 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(SummarizeJobRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _metrics.RecordInvocation();
        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        var tokens = 0;
        decimal cost = 0m;
        var newsTriggered = false;

        try
        {
            var candidates = await _summarizeService.GetPendingDocumentsAsync(request.ParserVersion, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await _summarizeService.SummarizeAsync(candidate, cancellationToken).ConfigureAwait(false);
                    processed++;
                    tokens += result.TotalTokens;
                    cost += result.TotalCost;
                    newsTriggered |= result.TriggerNewsWorkflow;
                }
                catch (InvalidByoKeyException ex)
                {
                    _logger.LogWarning(ex, "Skipping document {DocumentId} because BYO key is invalid", candidate.DocumentId);
                }
                catch (MissingFactsException ex)
                {
                    _logger.LogWarning(ex, "Skipping document {DocumentId} due to missing facts", candidate.DocumentId);
                }
                catch (QuotaExceededException ex)
                {
                    _logger.LogWarning(ex, "Quota exceeded when summarizing document {DocumentId}", candidate.DocumentId);
                }
            }

            stopwatch.Stop();
            _metrics.RecordSuccess(stopwatch.Elapsed, processed, tokens, cost);

            if (newsTriggered)
            {
                _jobScheduler.EnqueueNewsIngestion();
            }
        }
        catch (LlmRequestTimeoutException ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailure(stopwatch.Elapsed, "timeout");
            _logger.LogWarning(ex, "LLM timeout in summarize job");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            _logger.LogError(ex, "Summarize job failed");
            throw;
        }
    }
}
