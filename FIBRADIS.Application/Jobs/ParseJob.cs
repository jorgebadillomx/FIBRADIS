using System;
using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class ParseJob
{
    public const string QueueName = "parse";

    private readonly IDocumentParser _documentParser;
    private readonly IDocumentRepository _documentRepository;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDocumentParseMetricsCollector _metricsCollector;
    private readonly ILogger<ParseJob> _logger;
    private readonly IClock _clock;

    public ParseJob(
        IDocumentParser documentParser,
        IDocumentRepository documentRepository,
        IBackgroundJobClient jobClient,
        IDocumentParseMetricsCollector metricsCollector,
        IClock clock,
        ILogger<ParseJob> logger)
    {
        _documentParser = documentParser ?? throw new ArgumentNullException(nameof(documentParser));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _jobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 180 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(DocumentParseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _metricsCollector.RecordInvocation();

        var jobRunId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var startedAt = _clock.UtcNow;

        try
        {
            var result = await _documentParser.ParseAsync(request, ct).ConfigureAwait(false);
            stopwatch.Stop();

            if (!result.Success)
            {
                if (result.RequiresRetry)
                {
                    _metricsCollector.RecordRetry(result.FailureReason ?? "retry");
                    throw new InvalidOperationException(result.FailureReason ?? "Parse requires retry");
                }

                _metricsCollector.RecordFailure(stopwatch.Elapsed, result.FailureReason ?? "parse_failed");
                await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, false, result.FailureReason, ct).ConfigureAwait(false);
                _logger.LogWarning("parse failed for {DocumentId}: {Reason}", request.DocumentId, result.FailureReason);
                return;
            }

            var textRecord = result.TextRecord!;
            var document = result.Document!;

            _metricsCollector.RecordSuccess(textRecord.OcrUsed, textRecord.Pages, document.Confidence, stopwatch.Elapsed);

            await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, true, "parsed", ct).ConfigureAwait(false);

            _jobClient.Enqueue<FactsJob>(job => job.ExecuteAsync(
                new DocumentFactsRequest(request.DocumentId, document.ParserVersion),
                CancellationToken.None));

            _logger.LogInformation("parse completed for {DocumentId} kind={Kind} ticker={Ticker}", request.DocumentId, document.Kind, document.Ticker);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, false, ex.Message, ct).ConfigureAwait(false);
            _logger.LogError(ex, "parse failed for {DocumentId}", request.DocumentId);
            throw;
        }
    }

    private async Task RecordEventAsync(Guid jobRunId, Guid documentId, DateTimeOffset startedAt, TimeSpan duration, bool success, string? details, CancellationToken ct)
    {
        await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
        {
            JobRunId = jobRunId,
            DocumentId = documentId,
            Stage = "parse",
            Status = success ? "completed" : "failed",
            StartedAt = startedAt,
            CompletedAt = startedAt + duration,
            Success = success,
            Details = details
        }, ct).ConfigureAwait(false);
    }
}
