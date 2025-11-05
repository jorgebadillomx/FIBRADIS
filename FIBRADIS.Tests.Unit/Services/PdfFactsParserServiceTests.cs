using System.Collections.Generic;
using System.Text;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class PdfFactsParserServiceTests
{
    [Fact]
    public async Task ParseAsync_WithTextDocument_PersistsFacts()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var payload = "Reporte 1T2025 NAV/CBFI 25.5 NOI 1,500,000 AFFO 1,200,000 LTV 45% Ocupación 94% Dividendo 0.35";
        var content = Encoding.UTF8.GetBytes(payload);
        var document = new DocumentContent(documentId, "hash-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, content, false);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FUNO11",
            Url = "https://example.com/report.pdf",
            Hash = "hash-1",
            ParserVersion = "1.0"
        };

        var storage = new Mock<IDocumentStorage>(MockBehavior.Strict);
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var extractor = new Mock<IPdfTextExtractor>(MockBehavior.Strict);
        extractor.Setup(e => e.ExtractAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction(payload, Array.Empty<IReadOnlyList<string>>(), false, 1));

        var ocr = new Mock<IOcrProvider>(MockBehavior.Strict);
        ocr.Setup(o => o.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrExtractionResult(string.Empty, Array.Empty<IReadOnlyList<string>>(), 0));

        var repository = new Mock<IFactsRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentFactsRecord?)null);
        repository.Setup(r => r.MarkSupersededAsync("FUNO11", "1T2025", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.SaveDocumentFactsAsync(It.Is<DocumentFactsRecord>(record => !record.RequiresReview && record.Score == 100), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.AppendHistoryAsync(It.IsAny<FactsHistoryRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.SavePendingReviewAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordSuccess(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()));
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2025, 1, 5, 0, 0, 0, TimeSpan.Zero));

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns("req-123");

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        // Act
        var result = await service.ParseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("FUNO11", result.FibraTicker);
        Assert.Equal("1T2025", result.PeriodTag);
        Assert.Equal(25.5m, result.NavPerCbfi);
        Assert.Equal(1.5m, result.Noi);
        Assert.Equal(1.2m, result.Affo);
        Assert.Equal(0.45m, result.Ltv);
        Assert.Equal(0.94m, result.Occupancy);
        Assert.Equal(0.35m, result.Dividends);
        Assert.Equal(100, result.Score);

        repository.Verify();
    }

    [Fact]
    public async Task ParseAsync_WithTabularDocument_UsesTableValues()
    {
        var documentId = Guid.NewGuid();
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Métrica", "Valor" },
            new[] { "NAV/CBFI", "26.4" },
            new[] { "NOI", "900000" },
            new[] { "AFFO", "850000" },
            new[] { "LTV", "38%" },
            new[] { "Occupancy", "0.91" },
            new[] { "Dividends", "0.28" }
        };
        var content = Encoding.UTF8.GetBytes("Reporte trimestral 2T2025");
        var document = new DocumentContent(documentId, "hash-2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, content, false);

        var storage = new Mock<IDocumentStorage>();
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction("2T2025", table, false, 0.9));

        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrExtractionResult(string.Empty, Array.Empty<IReadOnlyList<string>>(), 0));

        var repository = new Mock<IFactsRepository>();
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentFactsRecord?)null);
        repository.Setup(r => r.MarkSupersededAsync("FIBRAMQ12", "2T2025", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.SaveDocumentFactsAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.AppendHistoryAsync(It.IsAny<FactsHistoryRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.SavePendingReviewAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordSuccess(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()));
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns("req-456");

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FIBRAMQ12",
            Hash = "hash-2",
            Url = "https://example.com/doc.pdf",
            ParserVersion = "1.0"
        };

        // Act
        var result = await service.ParseAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(26.4m, result.NavPerCbfi);
        Assert.Equal(0.9m, result.Noi);
        Assert.Equal(0.85m, result.Affo);
        Assert.Equal(0.38m, result.Ltv);
        Assert.Equal(0.91m, result.Occupancy);
        Assert.Equal(0.28m, result.Dividends);
    }

    [Fact]
    public async Task ParseAsync_WhenImageDocument_InvokesOcr()
    {
        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Imagen");
        var document = new DocumentContent(documentId, "hash-3", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, content, true);

        var storage = new Mock<IDocumentStorage>();
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction(string.Empty, Array.Empty<IReadOnlyList<string>>(), true, 0));

        var ocrText = "Reporte 3T2025 NAV/CBFI 24 NOI 600000 AFFO 550000 LTV 40% Occupancy 92% Dividendo 0.3";
        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.ExtractAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrExtractionResult(ocrText, Array.Empty<IReadOnlyList<string>>(), 0.8))
            .Verifiable();

        var repository = new Mock<IFactsRepository>();
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentFactsRecord?)null);
        repository.Setup(r => r.MarkSupersededAsync("FIBRAPL14", "3T2025", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.SaveDocumentFactsAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.AppendHistoryAsync(It.IsAny<FactsHistoryRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.SavePendingReviewAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordSuccess(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()));
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns("req-789");

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FIBRAPL14",
            Hash = "hash-3",
            ParserVersion = "1.0",
            Url = "https://example.com/ocr.pdf"
        };

        await service.ParseAsync(request, CancellationToken.None);

        ocr.Verify();
    }

    [Fact]
    public async Task ParseAsync_WithIncompleteFields_SendsToReview()
    {
        var documentId = Guid.NewGuid();
        var payload = "Reporte 4T2025 NAV/CBFI 20.4 NOI 800000";
        var content = Encoding.UTF8.GetBytes(payload);
        var document = new DocumentContent(documentId, "hash-4", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, content, false);

        var storage = new Mock<IDocumentStorage>();
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction(payload, Array.Empty<IReadOnlyList<string>>(), false, 0.9));

        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrExtractionResult(string.Empty, Array.Empty<IReadOnlyList<string>>(), 0));

        var repository = new Mock<IFactsRepository>();
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentFactsRecord?)null);
        repository.Setup(r => r.SavePendingReviewAsync(It.Is<DocumentFactsRecord>(record => record.RequiresReview && record.Score < 70), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.SaveDocumentFactsAsync(It.IsAny<DocumentFactsRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.MarkSupersededAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.AppendHistoryAsync(It.IsAny<FactsHistoryRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordSuccess(It.IsAny<TimeSpan>(), It.IsAny<int>(), It.IsAny<int>()));
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero));

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns("req-321");

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FIBRAHD18",
            Hash = "hash-4",
            Url = "https://example.com/incomplete.pdf",
            ParserVersion = "1.0"
        };

        var result = await service.ParseAsync(request, CancellationToken.None);

        Assert.True(result.Score < 70);
        repository.Verify();
    }

    [Fact]
    public async Task ParseAsync_WithHashMismatch_Throws()
    {
        var documentId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("NAV/CBFI 10");
        var document = new DocumentContent(documentId, "hash-real", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, content, false);

        var storage = new Mock<IDocumentStorage>();
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction("1T2025 NAV/CBFI 10", Array.Empty<IReadOnlyList<string>>(), false, 1));

        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OcrExtractionResult(string.Empty, Array.Empty<IReadOnlyList<string>>(), 0));

        var repository = new Mock<IFactsRepository>();
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentFactsRecord?)null);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns((string?)null);

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FUNO11",
            Hash = "hash-wrong",
            Url = "https://example.com",
            ParserVersion = "1.0"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ParseAsync(request, CancellationToken.None));
        metrics.Verify(m => m.RecordFailure(It.IsAny<TimeSpan>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ParseAsync_WhenExistingFacts_ReturnsCached()
    {
        var documentId = Guid.NewGuid();
        var existing = new DocumentFactsRecord(
            Guid.NewGuid(),
            documentId,
            "FUNO11",
            "2T2024",
            23.1m,
            0.8m,
            0.7m,
            0.4m,
            0.92m,
            0.25m,
            95,
            "https://example.com",
            "1.0",
            "hash-5",
            DateTimeOffset.UtcNow,
            false,
            false);

        var repository = new Mock<IFactsRepository>();
        repository.Setup(r => r.GetDocumentFactsAsync(documentId, "1.0", "hash-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var storage = new Mock<IDocumentStorage>(MockBehavior.Strict);
        var extractor = new Mock<IPdfTextExtractor>(MockBehavior.Strict);
        var ocr = new Mock<IOcrProvider>(MockBehavior.Strict);

        var metrics = new Mock<IFactsMetricsCollector>();
        metrics.Setup(m => m.RecordInvocation());
        metrics.Setup(m => m.RecordSuccess(It.IsAny<TimeSpan>(), existing.FieldsFound, existing.Score));
        metrics.Setup(m => m.RecordFailure(It.IsAny<TimeSpan>()));

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var correlation = new Mock<ICorrelationIdAccessor>();
        correlation.SetupGet(c => c.CorrelationId).Returns("req-cache");

        var logger = new Mock<ILogger<PdfFactsParserService>>();

        var service = new PdfFactsParserService(
            storage.Object,
            extractor.Object,
            ocr.Object,
            repository.Object,
            metrics.Object,
            clock.Object,
            correlation.Object,
            logger.Object);

        var request = new ParseFactsRequest
        {
            DocumentId = documentId,
            FibraTicker = "FUNO11",
            Hash = "hash-5",
            ParserVersion = "1.0",
            Url = "https://example.com"
        };

        var result = await service.ParseAsync(request, CancellationToken.None);

        Assert.Equal(existing.NavPerCbfi, result.NavPerCbfi);
        storage.VerifyNoOtherCalls();
    }
}
