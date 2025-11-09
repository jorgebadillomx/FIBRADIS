using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class ReportsJob
{
    public const string QueueName = "reports";
    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromDays(30);

    private readonly IDocumentDiscoveryService _discoveryService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IReportsDiscoveryMetricsCollector _metricsCollector;
    private readonly IClock _clock;
    private readonly IRobotsPolicy _robotsPolicy;
    private readonly ILogger<ReportsJob> _logger;

    public ReportsJob(
        IDocumentDiscoveryService discoveryService,
        IDocumentRepository documentRepository,
        IBackgroundJobClient jobClient,
        IReportsDiscoveryMetricsCollector metricsCollector,
        IClock clock,
        IRobotsPolicy robotsPolicy,
        ILogger<ReportsJob> logger)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _jobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _robotsPolicy = robotsPolicy ?? throw new ArgumentNullException(nameof(robotsPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 90, 180 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(ReportDiscoveryRequest request, CancellationToken ct = default)
    {
        _metricsCollector.RecordInvocation();
        var jobRunId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var since = request?.Since ?? _clock.UtcNow.AddDays(-7);
        var domains = request?.Domains?.ToArray();
        var discovered = 0;
        var skippedRobots = 0;
        var duplicates = 0;
        var newDocuments = new List<DocumentRecord>();
        var startedAt = _clock.UtcNow;

        try
        {
            var candidates = await _discoveryService.DiscoverAsync(since, domains, ct).ConfigureAwait(false);
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                discovered++;

                if (string.IsNullOrWhiteSpace(candidate.Url))
                {
                    continue;
                }

                if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                if (!await _robotsPolicy.IsAllowedAsync(uri, ct).ConfigureAwait(false))
                {
                    skippedRobots++;
                    continue;
                }

                var existing = await _documentRepository.GetByUrlAsync(candidate.Url, ct).ConfigureAwait(false);
                if (existing is not null && startedAt - existing.DiscoveredAt <= DiscoveryWindow)
                {
                    duplicates++;
                    continue;
                }

                var documentId = existing?.DocumentId ?? Guid.NewGuid();
                var confidence = ResolveConfidence(candidate.KindHint);
                var kind = ResolveKind(candidate.KindHint);
                var provenance = new DocumentProvenance
                {
                    Referer = candidate.Referer,
                    CrawlPath = candidate.CrawlPath,
                    RobotsOk = true,
                    AdditionalMetadata = new Dictionary<string, string>(candidate.Metadata, StringComparer.OrdinalIgnoreCase)
                };

                var document = new DocumentRecord
                {
                    DocumentId = documentId,
                    Url = candidate.Url,
                    SourceDomain = uri.Host,
                    Ticker = candidate.Ticker,
                    DiscoveredAt = startedAt,
                    PublishedAt = candidate.PublishedAt,
                    Status = DocumentStatus.DownloadQueued,
                    Kind = kind,
                    Confidence = confidence,
                    Provenance = provenance,
                    Metadata = provenance.AdditionalMetadata
                };

                if (existing is null)
                {
                    await _documentRepository.AddAsync(document, ct).ConfigureAwait(false);
                }
                else
                {
                    document = document with
                    {
                        Hash = existing.Hash,
                        ParserVersion = existing.ParserVersion,
                        Status = DocumentStatus.DownloadQueued
                    };
                    await _documentRepository.UpdateAsync(document, ct).ConfigureAwait(false);
                }

                newDocuments.Add(document);

                _jobClient.Enqueue<DownloadJob>(job => job.ExecuteAsync(
                    new DocumentDownloadRequest(document.DocumentId, document.Url),
                    CancellationToken.None));

                await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
                {
                    JobRunId = jobRunId,
                    DocumentId = document.DocumentId,
                    Stage = "reports",
                    Status = "queued",
                    StartedAt = startedAt,
                    CompletedAt = startedAt,
                    Success = true,
                    Details = "Descubrimiento registrado"
                }, ct).ConfigureAwait(false);
            }

            stopwatch.Stop();
            _metricsCollector.RecordSuccess(newDocuments.Count, skippedRobots, duplicates, stopwatch.Elapsed);

            _logger.LogInformation(
                "reports.discovery completed runId={JobRunId} discovered={Discovered} new={New} duplicates={Duplicates} skippedRobots={Skipped}",
                jobRunId,
                discovered,
                newDocuments.Count,
                duplicates,
                skippedRobots);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsCollector.RecordFailure(stopwatch.Elapsed, ex.GetType().Name);
            await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
            {
                JobRunId = jobRunId,
                DocumentId = Guid.Empty,
                Stage = "reports",
                Status = "failed",
                StartedAt = startedAt,
                CompletedAt = startedAt + stopwatch.Elapsed,
                Success = false,
                Details = ex.Message
            }, ct).ConfigureAwait(false);
            _logger.LogError(ex, "reports.discovery failed runId={JobRunId}", jobRunId);
            throw;
        }
    }

    private static decimal ResolveConfidence(string? kindHint)
    {
        if (string.IsNullOrWhiteSpace(kindHint))
        {
            return 0.4m;
        }

        return kindHint.Contains("hr", StringComparison.OrdinalIgnoreCase) ? 0.6m : 0.5m;
    }

    private static DocumentKind ResolveKind(string? kindHint)
    {
        if (string.IsNullOrWhiteSpace(kindHint))
        {
            return DocumentKind.Other;
        }

        if (kindHint.Contains("hr", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.HechoRelevante;
        }

        if (kindHint.Contains("q", StringComparison.OrdinalIgnoreCase) || kindHint.Contains("trim", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.Quarterly;
        }

        if (kindHint.Contains("present", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.Presentation;
        }

        if (kindHint.Contains("distrib", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.DistributionNotice;
        }

        return DocumentKind.Other;
    }
}
