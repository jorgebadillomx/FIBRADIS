using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Jobs;

public sealed class FactStatusJob
{
    public const string QueueName = "facts-status";

    private readonly IDocumentRepository _documentRepository;
    private readonly IClock _clock;
    private readonly ILogger<FactStatusJob> _logger;

    public FactStatusJob(
        IDocumentRepository documentRepository,
        IClock clock,
        ILogger<FactStatusJob> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Queue(QueueName)]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 60, 300 }, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task ExecuteAsync(FactStatusJobRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required", nameof(request));
        }

        var startedAt = _clock.UtcNow;
        var stage = string.IsNullOrWhiteSpace(request.Stage) ? "facts" : request.Stage;
        var status = string.IsNullOrWhiteSpace(request.Status)
            ? (request.Success ? "completed" : "failed")
            : request.Status;

        if (request.DocumentStatus.HasValue)
        {
            var document = await _documentRepository.GetByIdAsync(request.DocumentId, ct).ConfigureAwait(false);
            if (document is not null)
            {
                var parsedAt = document.ParsedAt;
                if (request.DocumentStatus is DocumentStatus.Parsed or DocumentStatus.FactsExtracted)
                {
                    parsedAt ??= startedAt;
                }

                var parserVersion = string.IsNullOrWhiteSpace(request.ParserVersion)
                    ? document.ParserVersion
                    : request.ParserVersion!;

                var hash = string.IsNullOrWhiteSpace(request.Hash)
                    ? document.Hash
                    : request.Hash;

                var updated = document with
                {
                    Status = request.DocumentStatus.Value,
                    FailureReason = request.Success ? null : request.Details,
                    ParserVersion = parserVersion,
                    Hash = hash,
                    ParsedAt = parsedAt
                };

                await _documentRepository.UpdateAsync(updated, ct).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Document {DocumentId} not found when recording facts status.", request.DocumentId);
            }
        }

        var completedAt = _clock.UtcNow;

        await _documentRepository.RecordJobEventAsync(new DocumentJobEvent
        {
            JobRunId = Guid.NewGuid(),
            DocumentId = request.DocumentId,
            Stage = stage,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Success = request.Success,
            Details = request.Details,
            ParserVersion = request.ParserVersion,
            Hash = request.Hash
        }, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Recorded facts status {Status} for document {DocumentId} (success={Success}, stage={Stage})",
            status,
            request.DocumentId,
            request.Success,
            stage);
    }
}
