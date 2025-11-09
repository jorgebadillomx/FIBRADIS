using System;
using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class FactsJob
{
    public const string QueueName = "facts";
    private const int ReviewThreshold = 70;

    private readonly IFactsExtractor _factsExtractor;
    private readonly IDocumentRepository _documentRepository;
    private readonly ISecuritiesRepository _securitiesRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDocumentFactsPipelineMetricsCollector _metricsCollector;
    private readonly ILogger<FactsJob> _logger;
    private readonly IClock _clock;

    public FactsJob(
        IFactsExtractor factsExtractor,
        IDocumentRepository documentRepository,
        ISecuritiesRepository securitiesRepository,
        IPortfolioRepository portfolioRepository,
        IBackgroundJobClient jobClient,
        IDocumentFactsPipelineMetricsCollector metricsCollector,
        IClock clock,
        ILogger<FactsJob> logger)
    {
        _factsExtractor = factsExtractor ?? throw new ArgumentNullException(nameof(factsExtractor));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _securitiesRepository = securitiesRepository ?? throw new ArgumentNullException(nameof(securitiesRepository));
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _jobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 120, 300 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(DocumentFactsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _metricsCollector.RecordInvocation();

        var jobRunId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var startedAt = _clock.UtcNow;

        try
        {
            var result = await _factsExtractor.ExtractAsync(request, ct).ConfigureAwait(false);
            stopwatch.Stop();

            if (!result.Success)
            {
                _metricsCollector.RecordFailure(stopwatch.Elapsed, result.FailureReason ?? "facts_failed");
                await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, false, result.FailureReason, ct).ConfigureAwait(false);
                _logger.LogWarning("facts extraction failed for {DocumentId}: {Reason}", request.DocumentId, result.FailureReason);
                return;
            }

            _metricsCollector.RecordSuccess(result.Score, result.RequiresReview, stopwatch.Elapsed);
            await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, true, result.RequiresReview ? "requires_review" : "facts_extracted", ct).ConfigureAwait(false);

            if (result.Facts is not null && result.Score >= ReviewThreshold && !result.RequiresReview)
            {
                await UpdateSecuritiesAsync(result.Facts, ct).ConfigureAwait(false);
                await EnqueuePortfolioRecalcAsync(result.Facts.FibraTicker, ct).ConfigureAwait(false);
            }

            _logger.LogInformation("facts extraction completed for {DocumentId} ticker={Ticker} score={Score}", request.DocumentId, result.Facts?.FibraTicker, result.Score);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, false, ex.Message, ct).ConfigureAwait(false);
            _logger.LogError(ex, "facts extraction failed for {DocumentId}", request.DocumentId);
            throw;
        }
    }

    private async Task UpdateSecuritiesAsync(DocumentFactsRecord facts, CancellationToken ct)
    {
        var metrics = new SecurityMetricsDto
        {
            NavPerCbfi = facts.NavPerCbfi,
            Noi = facts.Noi,
            Affo = facts.Affo,
            Ltv = facts.Ltv,
            Occupancy = facts.Occupancy,
            YieldTtm = facts.Dividends,
            Source = facts.SourceUrl,
            UpdatedAt = facts.ParsedAtUtc
        };

        await _securitiesRepository.UpdateMetricsAsync(facts.FibraTicker, metrics, ct).ConfigureAwait(false);
    }

    private async Task EnqueuePortfolioRecalcAsync(string ticker, CancellationToken ct)
    {
        var users = await _portfolioRepository.GetUsersHoldingTickerAsync(ticker, ct).ConfigureAwait(false);
        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            _jobClient.Enqueue<PortfolioRecalcJob>(job => job.ExecuteAsync(new PortfolioRecalcJobInput
            {
                UserId = user,
                Reason = "kpi",
                RequestedAt = _clock.UtcNow
            }, CancellationToken.None));
        }
    }

    private async Task RecordEventAsync(Guid jobRunId, Guid documentId, DateTimeOffset startedAt, TimeSpan duration, bool success, string? details, CancellationToken ct)
    {
        await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
        {
            JobRunId = jobRunId,
            DocumentId = documentId,
            Stage = "facts",
            Status = success ? "completed" : "failed",
            StartedAt = startedAt,
            CompletedAt = startedAt + duration,
            Success = success,
            Details = details
        }, ct).ConfigureAwait(false);
    }
}
