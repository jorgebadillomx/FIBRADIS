using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FIBRADIS.Tests.Unit.Jobs;

public sealed class DownloadJobTests
{
    [Fact]
    public async Task ExecuteAsync_MarksDuplicateHashAsSuperseded()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var downloader = new Mock<IDocumentDownloader>();
        var repository = new Mock<IDocumentRepository>();
        var storage = new Mock<IDocumentStorage>();
        var jobClient = new Mock<IBackgroundJobClient>();
        var metrics = new Mock<IDocumentDownloadMetricsCollector>();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);

        var document = new DocumentRecord
        {
            DocumentId = documentId,
            Url = "https://example.com/doc.pdf",
            Status = DocumentStatus.DownloadQueued,
            ParserVersion = "1.0"
        };

        repository.Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        repository.Setup(r => r.GetByHashAsync("DUPHASH", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord { DocumentId = Guid.NewGuid(), Hash = "DUPHASH" });
        repository.Setup(r => r.UpdateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord updated, CancellationToken _) => updated)
            .Verifiable();

        downloader.Setup(d => d.DownloadAsync(It.IsAny<DocumentDownloadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult
            {
                Success = true,
                Binary = new DocumentBinary
                {
                    DocumentId = documentId,
                    Hash = "DUPHASH",
                    Content = Array.Empty<byte>(),
                    ContentLength = 10,
                    DownloadedAt = now
                },
                Document = new DocumentRecord { PublishedAt = now }
            });

        var job = new DownloadJob(downloader.Object, repository.Object, storage.Object, jobClient.Object, metrics.Object, clock.Object, NullLogger<DownloadJob>.Instance);

        await job.ExecuteAsync(new DocumentDownloadRequest(documentId, document.Url), CancellationToken.None);

        metrics.Verify(m => m.RecordDuplicate("DUPHASH"), Times.Once);
        repository.Verify(r => r.UpdateAsync(It.Is<DocumentRecord>(d => d.Status == DocumentStatus.Superseded), It.IsAny<CancellationToken>()), Times.Once);
        jobClient.Verify(c => c.Enqueue(It.IsAny<System.Linq.Expressions.Expression<Func<ParseJob, Task>>>()), Times.Never);
        storage.Verify(s => s.SaveDocumentAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
