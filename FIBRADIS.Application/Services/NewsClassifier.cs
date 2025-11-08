using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FIBRADIS.Application.Services;

public sealed class NewsClassifier : INewsClassifier
{
    private readonly NewsClassifierOptions _options;
    private readonly ILogger<NewsClassifier> _logger;

    public NewsClassifier(IOptions<NewsClassifierOptions> options, ILogger<NewsClassifier> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<NewsClassification> ClassifyAsync(ExternalNewsArticle article, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);
        cancellationToken.ThrowIfCancellationRequested();

        var searchable = (article.Title + " " + article.Summary).ToLowerInvariant();

        var ticker = DetectTicker(searchable);
        var sector = DetectSector(searchable);
        var sentiment = DetectSentiment(searchable);
        var tokens = EstimateTokens(article);
        var cost = Math.Round(tokens / 1000m * _options.CostPerThousandTokensUsd, 6, MidpointRounding.AwayFromZero);

        _logger.LogDebug("Classified news {Title} ticker={Ticker} sector={Sector} sentiment={Sentiment}", article.Title, ticker, sector, sentiment);

        var result = new NewsClassification(ticker, sector, sentiment, tokens, cost, _options.Provider);
        return Task.FromResult(result);
    }

    private string? DetectTicker(string content)
    {
        foreach (var (ticker, keywords) in _options.FibraKeywords)
        {
            if (keywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return ticker;
            }
        }

        return null;
    }

    private string? DetectSector(string content)
    {
        foreach (var (sector, keywords) in _options.SectorKeywords)
        {
            if (keywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(sector.ToLowerInvariant());
            }
        }

        return null;
    }

    private NewsSentiment DetectSentiment(string content)
    {
        if (_options.PositiveKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return NewsSentiment.Positive;
        }

        if (_options.NegativeKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return NewsSentiment.Negative;
        }

        return NewsSentiment.Neutral;
    }

    private static int EstimateTokens(ExternalNewsArticle article)
    {
        var tokens = (article.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 2)
                     + article.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(12, (int)Math.Ceiling(tokens * 1.1));
    }
}
