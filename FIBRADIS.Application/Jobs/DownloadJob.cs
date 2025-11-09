using System;
using System.Collections.Generic;
using System.Diagnostics;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class DownloadJob
{
    public const string QueueName = "download";

    private readonly IDocumentDownloader _downloader;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentStorage _documentStorage;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IDocumentDownloadMetricsCollector _metricsCollector;
    private readonly IClock _clock;
    private readonly ILogger<DownloadJob> _logger;

    public DownloadJob(
        IDocumentDownloader downloader,
        IDocumentRepository documentRepository,
        IDocumentStorage documentStorage,
        IBackgroundJobClient jobClient,
        IDocumentDownloadMetricsCollector metricsCollector,
        IClock clock,
        ILogger<DownloadJob> logger)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _documentStorage = documentStorage ?? throw new ArgumentNullException(nameof(documentStorage));
        _jobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 90, 180 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(DocumentDownloadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _metricsCollector.RecordInvocation();

        var jobRunId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var startedAt = _clock.UtcNow;

        try
        {
            var document = await _documentRepository.GetByIdAsync(request.DocumentId, ct).ConfigureAwait(false)
                           ?? throw new InvalidOperationException($"Document {request.DocumentId} not found");

            var download = await _downloader.DownloadAsync(request, ct).ConfigureAwait(false);
            if (download.Ignored)
            {
                stopwatch.Stop();
                _metricsCollector.RecordIgnored(download.FailureReason ?? "ignored");
                await UpdateStatusAsync(document, DocumentStatus.Ignored, download.FailureReason, ct).ConfigureAwait(false);
                await RecordEventAsync(jobRunId, document.DocumentId, startedAt, stopwatch.Elapsed, false, download.FailureReason ?? "ignored", ct).ConfigureAwait(false);
                _logger.LogWarning("download skipped for {DocumentId}: {Reason}", document.DocumentId, download.FailureReason);
                return;
            }

            if (!download.Success)
            {
                throw new InvalidOperationException(download.FailureReason ?? "Download failed");
            }

            if (download.NotModified)
            {
                stopwatch.Stop();
                _metricsCollector.RecordSuccess(0, true, stopwatch.Elapsed);
                await RecordEventAsync(jobRunId, document.DocumentId, startedAt, stopwatch.Elapsed, true, "not-modified", ct).ConfigureAwait(false);
                _logger.LogInformation("download not-modified for {DocumentId}", document.DocumentId);
                return;
            }

            var binary = download.Binary ?? throw new InvalidOperationException("Missing download binary");
            var duplicate = await _documentRepository.GetByHashAsync(binary.Hash, ct).ConfigureAwait(false);
            if (duplicate is not null && duplicate.DocumentId != document.DocumentId)
            {
                stopwatch.Stop();
                _metricsCollector.RecordDuplicate(binary.Hash);

                var superseded = document with
                {
                    Hash = binary.Hash,
                    Status = DocumentStatus.Superseded,
                    FailureReason = $"Duplicated hash with {duplicate.DocumentId}",
                    DownloadedAt = startedAt
                };

                await _documentRepository.UpdateAsync(superseded, ct).ConfigureAwait(false);
                await RecordEventAsync(jobRunId, document.DocumentId, startedAt, stopwatch.Elapsed, true, "superseded", ct).ConfigureAwait(false);
                _logger.LogInformation("download duplicate hash for {DocumentId} -> {Existing}", document.DocumentId, duplicate.DocumentId);
                return;
            }

            var storedPublishedAt = download.Document?.PublishedAt ?? document.PublishedAt ?? startedAt;
            var content = new DocumentContent(
                document.DocumentId,
                binary.Hash,
                storedPublishedAt,
                storedPublishedAt,
                binary.Content,
                binary.IsImageBased);

            await _documentStorage.SaveDocumentAsync(content, ct).ConfigureAwait(false);

            var updated = document with
            {
                Hash = binary.Hash,
                Status = DocumentStatus.Downloaded,
                DownloadedAt = binary.DownloadedAt,
                ContentType = download.Document?.ContentType ?? document.ContentType,
                Confidence = Math.Max(document.Confidence, 0.5m),
                ETag = download.Document?.Provenance?.ETag ?? document.ETag,
                Provenance = MergeProvenance(document.Provenance, download.Document?.Provenance),
                ContentLength = binary.ContentLength,
                PublishedAt = download.Document?.PublishedAt ?? document.PublishedAt
            };

            await _documentRepository.UpdateAsync(updated, ct).ConfigureAwait(false);

            stopwatch.Stop();
            _metricsCollector.RecordSuccess(binary.ContentLength, false, stopwatch.Elapsed);

            await RecordEventAsync(jobRunId, document.DocumentId, startedAt, stopwatch.Elapsed, true, "downloaded", ct).ConfigureAwait(false);

            _jobClient.Enqueue<ParseJob>(job => job.ExecuteAsync(
                new DocumentParseRequest(document.DocumentId, updated.ParserVersion),
                CancellationToken.None));

            _logger.LogInformation("download completed for {DocumentId} bytes={Bytes}", document.DocumentId, binary.ContentLength);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            await RecordEventAsync(jobRunId, request.DocumentId, startedAt, stopwatch.Elapsed, false, ex.Message, ct).ConfigureAwait(false);
            _logger.LogError(ex, "download failed for {DocumentId}", request.DocumentId);
            throw;
        }
    }

    private async Task UpdateStatusAsync(DocumentRecord document, DocumentStatus status, string? reason, CancellationToken ct)
    {
        var updated = document with
        {
            Status = status,
            FailureReason = reason,
            DownloadedAt = _clock.UtcNow
        };

        await _documentRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
    }

    private async Task RecordEventAsync(Guid jobRunId, Guid documentId, DateTimeOffset startedAt, TimeSpan duration, bool success, string? details, CancellationToken ct)
    {
        await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
        {
            JobRunId = jobRunId,
            DocumentId = documentId,
            Stage = "download",
            Status = success ? "completed" : "failed",
            StartedAt = startedAt,
            CompletedAt = startedAt + duration,
            Success = success,
            Details = details
        }, ct).ConfigureAwait(false);
    }

    private static DocumentProvenance MergeProvenance(DocumentProvenance existing, DocumentProvenance? incoming)
    {
        if (incoming is null)
        {
            return existing;
        }

        var metadata = new Dictionary<string, string>(existing.AdditionalMetadata, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in incoming.AdditionalMetadata)
        {
            metadata[pair.Key] = pair.Value;
        }

        return new DocumentProvenance
        {
            Referer = incoming.Referer ?? existing.Referer,
            CrawlPath = incoming.CrawlPath ?? existing.CrawlPath,
            RobotsOk = incoming.RobotsOk,
            ETag = incoming.ETag ?? existing.ETag,
            AdditionalMetadata = metadata
        };
    }
}
