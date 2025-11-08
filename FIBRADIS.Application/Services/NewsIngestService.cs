using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.Extensions.Logging;

namespace FIBRADIS.Application.Services;

public sealed class NewsIngestService : INewsIngestService
{
    private readonly IReadOnlyList<INewsSourceClient> _sources;
    private readonly INewsRepository _newsRepository;
    private readonly INewsClassifier _classifier;
    private readonly ILLMUsageTracker _usageTracker;
    private readonly IClock _clock;
    private readonly ILogger<NewsIngestService> _logger;

    public NewsIngestService(
        IEnumerable<INewsSourceClient> sources,
        INewsRepository newsRepository,
        INewsClassifier classifier,
        ILLMUsageTracker usageTracker,
        IClock clock,
        ILogger<NewsIngestService> logger)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
        _newsRepository = newsRepository ?? throw new ArgumentNullException(nameof(newsRepository));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _usageTracker = usageTracker ?? throw new ArgumentNullException(nameof(usageTracker));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<NewsIngestionResult> IngestAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var created = new List<NewsRecord>();
        var duplicates = 0;
        var downloaded = 0;
        var tokens = 0;
        decimal cost = 0m;

        foreach (var source in _sources)
        {
            IReadOnlyList<ExternalNewsArticle> articles;
            try
            {
                articles = await source.FetchAsync(since, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download news from source {Source}", source.GetType().Name);
                continue;
            }

            downloaded += articles.Count;
            foreach (var article in articles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hash = ComputeHash(article.Url);
                var existing = await _newsRepository.GetByUrlHashAsync(hash, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    duplicates++;
                    continue;
                }

                var classification = await _classifier.ClassifyAsync(article, cancellationToken).ConfigureAwait(false);
                tokens += classification.TokensUsed;
                cost += classification.CostUsd;

                var record = new NewsRecord
                {
                    Title = article.Title,
                    Summary = article.Summary,
                    Url = article.Url,
                    UrlHash = hash,
                    PublishedAt = article.PublishedAt,
                    Source = article.Source,
                    FibraTicker = classification.FibraTicker,
                    Sector = classification.Sector,
                    Sentiment = classification.Sentiment,
                    Status = NewsStatus.Pending,
                    TokensUsed = classification.TokensUsed,
                    CostUsd = classification.CostUsd,
                    CreatedAt = _clock.UtcNow,
                    CreatedBy = "system"
                };

                await _newsRepository.SaveAsync(record, cancellationToken).ConfigureAwait(false);
                created.Add(record);

                await _usageTracker.RecordUsageAsync("system", classification.Provider, classification.TokensUsed, classification.CostUsd, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "News ingestion complete. downloaded={Downloaded} stored={Stored} duplicates={Duplicates} tokens={Tokens} cost={Cost}",
            downloaded,
            created.Count,
            duplicates,
            tokens,
            cost);

        return new NewsIngestionResult(created, downloaded, duplicates, tokens, cost);
    }

    private static string ComputeHash(string url)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(url);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
