using System.Linq;
using System.Text;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Integration;

public sealed class PdfFactsParserServiceIntegrationTests
{
    [Fact]
    public async Task ParseFacts_Pipeline_SavesFactsAndHistory()
    {
        var storage = new InMemoryDocumentStorage();
        var repository = new InMemoryFactsRepository();
        var extractor = new SimplePdfTextExtractor();
        var ocr = new InMemoryOcrProvider();
        var metrics = new FactsMetricsCollector();
        var clock = new TestClock(new DateTimeOffset(2025, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var correlation = new TestCorrelationIdAccessor("corr-int-1");
        var logger = NullLogger<PdfFactsParserService>.Instance;

        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Reporte 1T2025 NAV/CBFI 28 NOI 1800000 AFFO 1500000 LTV 42% Ocupación 95% Dividendo 0.4");
        storage.AddOrUpdate(new DocumentContent(documentId, "hash-int-1", clock.UtcNow, clock.UtcNow, content, false));

        var service = CreateService(storage, extractor, ocr, repository, metrics, clock, correlation, logger);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FUNO11",
            Hash = "hash-int-1",
            Url = "https://example.com/report.pdf",
            ParserVersion = "1.0"
        };

        var result = await service.ParseAsync(request, CancellationToken.None);

        Assert.Equal("FUNO11", result.FibraTicker);
        Assert.Equal("1T2025", result.PeriodTag);
        Assert.Equal(28m, result.NavPerCbfi);
        Assert.Equal(1.8m, result.Noi);
        Assert.Equal(1.5m, result.Affo);
        Assert.Equal(0.42m, result.Ltv);
        Assert.Equal(0.95m, result.Occupancy);
        Assert.Equal(0.4m, result.Dividends);
        Assert.True(result.Score >= 70);

        var stored = await repository.GetDocumentFactsAsync(documentId, "1.0", "hash-int-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.False(stored!.RequiresReview);

        var history = repository.History;
        Assert.Single(history);
        Assert.Equal(result.PeriodTag, history.First().PeriodTag);

        var snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.Invocations);
        Assert.Equal(0, snapshot.Failures);
    }

    [Fact]
    public async Task ParseFacts_Idempotent_ReturnsCachedData()
    {
        var storage = new InMemoryDocumentStorage();
        var repository = new InMemoryFactsRepository();
        var extractor = new SimplePdfTextExtractor();
        var ocr = new InMemoryOcrProvider();
        var metrics = new FactsMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var correlation = new TestCorrelationIdAccessor("corr-int-2");
        var logger = NullLogger<PdfFactsParserService>.Instance;

        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Reporte 2T2025 NAV/CBFI 30 NOI 1900000 AFFO 1600000 LTV 41% Occupancy 93% Dividendo 0.42");
        storage.AddOrUpdate(new DocumentContent(documentId, "hash-int-2", clock.UtcNow, clock.UtcNow, content, false));

        var service = CreateService(storage, extractor, ocr, repository, metrics, clock, correlation, logger);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FIBRAMQ12",
            Hash = "hash-int-2",
            Url = "https://example.com/report2.pdf",
            ParserVersion = "1.0"
        };

        var first = await service.ParseAsync(request, CancellationToken.None);
        var second = await service.ParseAsync(request, CancellationToken.None);

        Assert.Equal(first.NavPerCbfi, second.NavPerCbfi);
        Assert.Equal(2, metrics.Snapshot().Invocations);
    }

    [Fact]
    public async Task ParseFacts_ImageDocument_UsesOcrFallback()
    {
        var storage = new InMemoryDocumentStorage();
        var repository = new InMemoryFactsRepository();
        var extractor = new SimplePdfTextExtractor();
        var ocr = new InMemoryOcrProvider();
        var metrics = new FactsMetricsCollector();
        var clock = new TestClock(new DateTimeOffset(2025, 3, 15, 0, 0, 0, TimeSpan.Zero));
        var correlation = new TestCorrelationIdAccessor("corr-int-3");
        var logger = NullLogger<PdfFactsParserService>.Instance;

        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("binary-image");
        storage.AddOrUpdate(new DocumentContent(documentId, "hash-int-3", clock.UtcNow, clock.UtcNow, content, true));
        ocr.SetResult(content, new OcrExtractionResult("Reporte 3T2025 NAV/CBFI 22 NOI 1100000 AFFO 980000 LTV 39% Ocupación 91% Dividendo 0.31", Array.Empty<IReadOnlyList<string>>(), 0.85));

        var service = CreateService(storage, extractor, ocr, repository, metrics, clock, correlation, logger);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FIBRAPL14",
            Hash = "hash-int-3",
            Url = "https://example.com/ocr.pdf",
            ParserVersion = "1.0"
        };

        var result = await service.ParseAsync(request, CancellationToken.None);

        Assert.Equal(0.31m, result.Dividends);
        Assert.True(result.Score >= 70);
    }

    private static PdfFactsParserService CreateService(
        IDocumentStorage storage,
        IPdfTextExtractor extractor,
        InMemoryOcrProvider ocr,
        InMemoryFactsRepository repository,
        FactsMetricsCollector metrics,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ILogger<PdfFactsParserService> logger)
    {
        return new PdfFactsParserService(
            storage,
            extractor,
            ocr,
            repository,
            metrics,
            clock,
            correlation,
            logger);
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }
    }

    private sealed class TestCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public TestCorrelationIdAccessor(string? correlationId)
        {
            CorrelationId = correlationId;
        }

        public string? CorrelationId { get; }
    }
}
