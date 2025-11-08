using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class NewsIngestServiceTests
{
    [Fact]
    public async Task IngestAsync_DeduplicatesByUrlHash()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var usageTracker = new Mock<ILLMUsageTracker>();
        var logger = new Mock<ILogger<NewsIngestService>>();

        var repository = new InMemoryNewsRepository();
        var classifier = new Mock<INewsClassifier>();
        classifier.Setup(c => c.ClassifyAsync(It.IsAny<ExternalNewsArticle>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NewsClassification("FUNO11", "Industrial", NewsSentiment.Positive, 120, 0.15m, "openai"));

        var article = new ExternalNewsArticle("FUNO crece", "Resumen", "https://news.example.com/a", clock.Object.UtcNow, "Eco");
        await repository.SaveAsync(new NewsRecord
        {
            Title = article.Title,
            Summary = article.Summary,
            Url = article.Url,
            UrlHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(article.Url))),
            PublishedAt = article.PublishedAt,
            Source = article.Source,
            FibraTicker = "FUNO11",
            Sector = "Industrial",
            Sentiment = NewsSentiment.Positive,
            Status = NewsStatus.Published,
            TokensUsed = 100,
            CostUsd = 0.1m,
            CreatedAt = clock.Object.UtcNow,
            CreatedBy = "system"
        }, CancellationToken.None);

        var source = new Mock<INewsSourceClient>();
        source.Setup(s => s.FetchAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                article,
                new ExternalNewsArticle("FUNO duplica NOI", "Otro", article.Url, clock.Object.UtcNow, "Eco")
            });

        var service = new NewsIngestService(new[] { source.Object }, repository, classifier.Object, usageTracker.Object, clock.Object, logger.Object);

        var result = await service.IngestAsync(clock.Object.UtcNow.AddHours(-48), CancellationToken.None);

        Assert.Equal(1, result.PendingNews.Count);
        Assert.Equal(1, result.Duplicates);
        usageTracker.Verify(t => t.RecordUsageAsync("system", "openai", 120, 0.15m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestAsync_ClassifiesTickerSectorAndSentiment()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 3, 2, 9, 0, 0, TimeSpan.Zero));
        var usageTracker = new Mock<ILLMUsageTracker>();
        var logger = new Mock<ILogger<NewsIngestService>>();
        var repository = new InMemoryNewsRepository();
        var classifier = new NewsClassifier(Options.Create(new NewsClassifierOptions()), new Mock<ILogger<NewsClassifier>>().Object);

        var source = new Mock<INewsSourceClient>();
        source.Setup(s => s.FetchAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ExternalNewsArticle(
                    "Fibra Uno anuncia expansión industrial",
                    "La expansión refuerza su portafolio logístico",
                    "https://news.example.com/funo",
                    clock.Object.UtcNow,
                    "El Economista")
            });

        var service = new NewsIngestService(new[] { source.Object }, repository, classifier, usageTracker.Object, clock.Object, logger.Object);

        var result = await service.IngestAsync(clock.Object.UtcNow.AddHours(-48), CancellationToken.None);

        Assert.Single(result.PendingNews);
        var stored = (await repository.GetPendingAsync(CancellationToken.None)).Single();
        Assert.Equal("FUNO11", stored.FibraTicker);
        Assert.Equal("Industrial", stored.Sector);
        Assert.Equal(NewsSentiment.Positive, stored.Sentiment);
        usageTracker.Verify(t => t.RecordUsageAsync("system", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
