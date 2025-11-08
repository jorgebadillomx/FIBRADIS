using System;
using System.Collections.Generic;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class NewsCuratorService : INewsCuratorService
{
    private readonly INewsRepository _newsRepository;
    private readonly IAuditService _auditService;
    private readonly IClock _clock;
    private readonly ILogger<NewsCuratorService> _logger;

    public NewsCuratorService(
        INewsRepository newsRepository,
        IAuditService auditService,
        IClock clock,
        ILogger<NewsCuratorService> logger)
    {
        _newsRepository = newsRepository ?? throw new ArgumentNullException(nameof(newsRepository));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<NewsRecord>> GetPendingAsync(CancellationToken cancellationToken)
        => _newsRepository.GetPendingAsync(cancellationToken);

    public async Task<NewsRecord> ApproveAsync(Guid newsId, NewsCuratorContext context, CancellationToken cancellationToken)
    {
        var record = await EnsureExists(newsId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var updated = record with
        {
            Status = NewsStatus.Published,
            UpdatedAt = now,
            UpdatedBy = context.UserId
        };

        await _newsRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await RecordAuditAsync(context, "published", updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("News {NewsId} approved by {UserId}", newsId, context.UserId);
        return updated;
    }

    public async Task<NewsRecord> IgnoreAsync(Guid newsId, NewsCuratorContext context, CancellationToken cancellationToken)
    {
        var record = await EnsureExists(newsId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var updated = record with
        {
            Status = NewsStatus.Ignored,
            UpdatedAt = now,
            UpdatedBy = context.UserId
        };

        await _newsRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await RecordAuditAsync(context, "ignored", updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("News {NewsId} ignored by {UserId}", newsId, context.UserId);
        return updated;
    }

    public async Task<NewsRecord> UpdateAsync(Guid newsId, NewsUpdate update, NewsCuratorContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var record = await EnsureExists(newsId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        var updated = record with
        {
            Title = string.IsNullOrWhiteSpace(update.Title) ? record.Title : update.Title!,
            Summary = string.IsNullOrWhiteSpace(update.Summary) ? record.Summary : update.Summary!,
            FibraTicker = update.FibraTicker ?? record.FibraTicker,
            Sector = update.Sector ?? record.Sector,
            Sentiment = update.Sentiment ?? record.Sentiment,
            UpdatedAt = now,
            UpdatedBy = context.UserId
        };

        await _newsRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        await RecordAuditAsync(context, "updated", updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("News {NewsId} updated by {UserId}", newsId, context.UserId);
        return updated;
    }

    private async Task<NewsRecord> EnsureExists(Guid id, CancellationToken cancellationToken)
    {
        var record = await _newsRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new KeyNotFoundException($"News entry {id} was not found");
        }

        return record;
    }

    private Task RecordAuditAsync(NewsCuratorContext context, string action, NewsRecord record, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["newsId"] = record.Id,
            ["ticker"] = record.FibraTicker,
            ["status"] = record.Status.ToString(),
            ["sentiment"] = record.Sentiment.ToString(),
            ["source"] = record.Source
        };

        var entry = new AuditEntry(context.UserId, "news.curated", action, context.IpAddress, metadata);
        return _auditService.RecordAsync(entry, cancellationToken);
    }
}
