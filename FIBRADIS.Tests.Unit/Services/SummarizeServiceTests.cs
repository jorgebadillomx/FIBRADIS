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

public sealed class SummarizeServiceTests
{
    [Fact]
    public async Task SummarizeAsync_GeneratesSummariesAndLogsUsage()
    {
        // Arrange
        var summaryRepository = new Mock<ISummaryRepository>();
        var factsRepository = new Mock<IFactsRepository>();
        var usageTracker = new Mock<ILLMUsageTracker>();
        var auditService = new Mock<IAuditService>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 3, 31, 12, 0, 0, TimeSpan.Zero));
        var logger = new Mock<ILogger<SummarizeService>>();
        var options = Options.Create(new SummarizeServiceOptions { SystemKey = "system-key" });

        var service = new SummarizeService(
            summaryRepository.Object,
            factsRepository.Object,
            usageTracker.Object,
            auditService.Object,
            clock.Object,
            options,
            logger.Object);

        var documentId = Guid.NewGuid();
        var facts = new DocumentFactsRecord(
            Guid.NewGuid(),
            documentId,
            "FUNO11",
            "2024Q1",
            12.5m,
            34.6m,
            18.2m,
            0.45m,
            0.93m,
            0.85m,
            100,
            "https://example.com/report.pdf",
            "parser-v1",
            "hash-123",
            DateTimeOffset.UtcNow,
            false,
            false);

        var candidate = new DocumentSummaryCandidate(
            documentId,
            "FUNO11",
            "2024Q1",
            "parser-v1",
            "hash-123",
            facts,
            "Reporte trimestral",
            "La FIBRA anunció una expansión industrial con nuevas adquisiciones.",
            "system",
            "user-123",
            "valid-byo-key-12345",
            20_000,
            "openai",
            "127.0.0.1");

        factsRepository.Setup(r => r.GetDocumentFactsAsync(documentId, "parser-v1", "hash-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Act
        var result = await service.SummarizeAsync(candidate, CancellationToken.None);

        // Assert
        summaryRepository.Verify(r => r.SaveSummaryAsync(It.Is<SummaryRecord>(s => s.Type == SummaryType.Public && s.SourceDocumentId == documentId), It.IsAny<CancellationToken>()), Times.Once);
        summaryRepository.Verify(r => r.SaveSummaryAsync(It.Is<SummaryRecord>(s => s.Type == SummaryType.Private && s.SourceDocumentId == documentId && s.Content.Contains("expansión", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Once);
        summaryRepository.Verify(r => r.MarkDocumentSummarizedAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
        usageTracker.Verify(t => t.RecordUsageAsync("user-123", "openai", result.TotalTokens, result.TotalCost, It.IsAny<CancellationToken>()), Times.Once);
        auditService.Verify(a => a.RecordAsync(It.Is<AuditEntry>(entry => entry.Action == "summarize.generated" && entry.UserId == "user-123"), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(result.TriggerNewsWorkflow);
        Assert.True(result.TotalTokens > 0);
        Assert.True(result.TotalCost > 0m);
    }
}
