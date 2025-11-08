using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using FIBRADIS.Application.Services.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FIBRADIS.Tests.Integration;

public sealed class SummarizeAndNewsPipelineIntegrationTests
{
    [Fact]
    public async Task Pipeline_GeneratesSummariesAndPendingNews()
    {
        // Arrange summarization components
        var summaryRepository = new InMemorySummaryRepository();
        var factsRepository = new InMemoryFactsRepository();
        var usageTracker = new Mock<ILLMUsageTracker>();
        var auditService = new Mock<IAuditService>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 3, 5, 9, 0, 0, TimeSpan.Zero));
        var summarizeOptions = Options.Create(new SummarizeServiceOptions { SystemKey = "system-key" });
        var summarizeLogger = new Mock<ILogger<SummarizeService>>();
        var summarizeService = new SummarizeService(
            summaryRepository,
            factsRepository,
            usageTracker.Object,
            auditService.Object,
            clock.Object,
            summarizeOptions,
            summarizeLogger.Object);

        var documentId = Guid.NewGuid();
        var facts = new DocumentFactsRecord(
            Guid.NewGuid(),
            documentId,
            "FUNO11",
            "2024Q1",
            15.2m,
            40.3m,
            21.7m,
            0.42m,
            0.95m,
            0.88m,
            90,
            "https://example.com/report",
            "parser-v1",
            "hash-1",
            clock.Object.UtcNow,
            false,
            false);

        await factsRepository.SaveDocumentFactsAsync(facts, CancellationToken.None);

        var candidate = new DocumentSummaryCandidate(
            documentId,
            "FUNO11",
            "2024Q1",
            "parser-v1",
            "hash-1",
            facts,
            "Reporte FUNO",
            "Se anunció una expansión logística significativa en el portafolio.",
            "system",
            "user-abc",
            "byo-key-valid-12345",
            5000,
            "openai",
            "127.0.0.1");
        summaryRepository.AddCandidate(candidate);

        var scheduler = new TestJobScheduler();
        var summarizeJobLogger = new Mock<ILogger<SummarizeJob>>();
        var summarizeJob = new SummarizeJob(summarizeService, new SummarizeMetricsCollector(), scheduler, summarizeJobLogger.Object);

        // Act - run summarize job
        await summarizeJob.ExecuteAsync(new SummarizeJobRequest("parser-v1"), CancellationToken.None);

        // Assert summaries
        var summaries = summaryRepository.GetSummaries();
        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, summary => summary.Type == SummaryType.Public);
        Assert.Contains(summaries, summary => summary.Type == SummaryType.Private);
        Assert.True(scheduler.NewsRequested);

        // Arrange news ingestion
        var newsRepository = new InMemoryNewsRepository();
        var newsClock = new Mock<IClock>();
        newsClock.SetupGet(c => c.UtcNow).Returns(clock.Object.UtcNow);
        var newsUsageTracker = new Mock<ILLMUsageTracker>();
        var newsLogger = new Mock<ILogger<NewsIngestService>>();
        var classifier = new NewsClassifier(Options.Create(new NewsClassifierOptions()), new Mock<ILogger<NewsClassifier>>().Object);

        var source = new Mock<INewsSourceClient>();
        source.Setup(s => s.FetchAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ExternalNewsArticle(
                    "Fibra Uno anuncia nueva expansión",
                    "El plan de expansión incrementa el NOI esperado",
                    "https://news.example.com/funo-expansion",
                    newsClock.Object.UtcNow,
                    "El Economista")
            });

        var newsService = new NewsIngestService(new[] { source.Object }, newsRepository, classifier, newsUsageTracker.Object, newsClock.Object, newsLogger.Object);

        var newsResult = await newsService.IngestAsync(newsClock.Object.UtcNow.AddHours(-48), CancellationToken.None);

        Assert.Single(newsResult.PendingNews);
        var pending = await newsRepository.GetPendingAsync(CancellationToken.None);
        Assert.Single(pending);
        Assert.Equal("FUNO11", pending[0].FibraTicker);
        Assert.Equal(NewsSentiment.Positive, pending[0].Sentiment);
    }

    private sealed class TestJobScheduler : IJobScheduler
    {
        public bool NewsRequested { get; private set; }

        public void EnqueuePortfolioRecalc(string userId, string reason, DateTimeOffset requestedAt)
        {
        }

        public void EnqueueNewsIngestion()
        {
            NewsRequested = true;
        }

        public void EnqueueSummarize(string parserVersion)
        {
        }
    }
}
