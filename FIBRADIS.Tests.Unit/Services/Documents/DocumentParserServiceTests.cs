using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FIBRADIS.Tests.Unit.Services.Documents;

public sealed class DocumentParserServiceTests
{
    [Fact]
    public async Task ParseAsync_StoresTextAndUpdatesDocument()
    {
        var documentId = Guid.NewGuid();
        var clock = new Mock<IClock>();
        var now = DateTimeOffset.UtcNow;
        clock.Setup(c => c.UtcNow).Returns(now);

        var repository = new Mock<IDocumentRepository>();
        repository.Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                DocumentId = documentId,
                Url = "https://example.com/doc.pdf",
                Hash = "ABC123",
                ParserVersion = "1.0",
                Status = DocumentStatus.Downloaded,
                Metadata = new Dictionary<string, string>()
            });

        var storage = new Mock<IDocumentStorage>();
        storage.Setup(s => s.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent(documentId, "ABC123", now, now, Encoding.UTF8.GetBytes("Hecho relevante 1T 2024"), false));

        var extractor = new Mock<IPdfTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfTextExtraction("Hecho relevante 1T 2024", Array.Empty<IReadOnlyList<string>>(), false, 0.9));

        var ocr = new Mock<IOcrProvider>(MockBehavior.Strict);

        var classifier = new Mock<IDocumentClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<DocumentTextRecord>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns(new DocumentClassificationResult(DocumentKind.HechoRelevante, "FIBRAA", "2024Q1", 0.9m));

        repository.Setup(r => r.SaveTextAsync(It.IsAny<DocumentTextRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.UpdateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord record, CancellationToken _) => record)
            .Verifiable();

        var service = new DocumentParserService(repository.Object, storage.Object, extractor.Object, ocr.Object, classifier.Object, clock.Object, NullLogger<DocumentParserService>.Instance);

        var result = await service.ParseAsync(new DocumentParseRequest(documentId, "1.1"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.TextRecord);
        Assert.Equal("FIBRAA", result.Document?.Ticker);
        Assert.Equal(DocumentStatus.Parsed, result.Document?.Status);
        repository.Verify();
    }
}
