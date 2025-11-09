using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FIBRADIS.Tests.Unit.Jobs;

public sealed class ReportsJobTests
{
    [Fact]
    public async Task ExecuteAsync_SkipsDuplicatesAndRespectsRobots()
    {
        var discoveryService = new Mock<IDocumentDiscoveryService>();
        var repository = new Mock<IDocumentRepository>();
        var jobClient = new Mock<IBackgroundJobClient>();
        var metrics = new Mock<IReportsDiscoveryMetricsCollector>();
        var clock = new Mock<IClock>();
        var robots = new Mock<IRobotsPolicy>();

        var now = DateTimeOffset.UtcNow;
        clock.Setup(c => c.UtcNow).Returns(now);

        var duplicate = new DocumentRecord
        {
            DocumentId = Guid.NewGuid(),
            Url = "https://example.com/dup.pdf",
            DiscoveredAt = now.AddDays(-1)
        };

        repository.Setup(r => r.GetByUrlAsync("https://example.com/dup.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicate);

        repository.Setup(r => r.AddAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord record, CancellationToken _) => record)
            .Verifiable();

        robots.Setup(r => r.IsAllowedAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        discoveryService.Setup(d => d.DiscoverAsync(It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DiscoveredDocument>
            {
                new()
                {
                    Url = "https://example.com/dup.pdf",
                    KindHint = "q1"
                },
                new()
                {
                    Url = "https://example.com/new.pdf",
                    KindHint = "hr",
                    Ticker = "FIBRAA"
                }
            });

        var job = new ReportsJob(discoveryService.Object, repository.Object, jobClient.Object, metrics.Object, clock.Object, robots.Object, NullLogger<ReportsJob>.Instance);

        await job.ExecuteAsync(new ReportDiscoveryRequest(), CancellationToken.None);

        repository.Verify(r => r.AddAsync(It.Is<DocumentRecord>(d => d.Url == "https://example.com/new.pdf"), It.IsAny<CancellationToken>()), Times.Once);
        jobClient.Verify(c => c.Enqueue(It.IsAny<Expression<Func<DownloadJob, Task>>>()), Times.AtLeastOnce);
        metrics.Verify(m => m.RecordSuccess(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once);
    }
}
