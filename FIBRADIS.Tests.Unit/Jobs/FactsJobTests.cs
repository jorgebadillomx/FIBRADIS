using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Jobs;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Models.Documents;
using FIBRADIS.Application.Ports;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FIBRADIS.Tests.Unit.Jobs;

public sealed class FactsJobTests
{
    [Fact]
    public async Task ExecuteAsync_UpdatesSecuritiesAndEnqueuesRecalc()
    {
        var extractor = new Mock<IFactsExtractor>();
        var repository = new Mock<IDocumentRepository>();
        var securities = new Mock<ISecuritiesRepository>();
        var portfolios = new Mock<IPortfolioRepository>();
        var jobClient = new Mock<IBackgroundJobClient>();
        var metrics = new Mock<IDocumentFactsPipelineMetricsCollector>();
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var factsRecord = new DocumentFactsRecord(Guid.NewGuid(), Guid.NewGuid(), "FIBRAA", "2024Q1", 12m, 10m, 9m, 0.35m, 0.9m, 0.5m, 85, "https://example.com", "1.0", "HASH", DateTimeOffset.UtcNow, false, false);
        extractor.Setup(e => e.ExtractAsync(It.IsAny<DocumentFactsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FactsResult
            {
                Success = true,
                Score = 85,
                RequiresReview = false,
                Facts = factsRecord
            });

        portfolios.Setup(p => p.GetUsersHoldingTickerAsync("FIBRAA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "user-1" });

        repository.Setup(r => r.RecordJobEventAsync(It.IsAny<DocumentJobEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var job = new FactsJob(extractor.Object, repository.Object, securities.Object, portfolios.Object, jobClient.Object, metrics.Object, clock.Object, NullLogger<FactsJob>.Instance);

        await job.ExecuteAsync(new DocumentFactsRequest(factsRecord.DocumentId, "1.0"), CancellationToken.None);

        securities.Verify(s => s.UpdateMetricsAsync("FIBRAA", It.IsAny<SecurityMetricsDto>(), It.IsAny<CancellationToken>()), Times.Once);
        jobClient.Verify(c => c.Enqueue(It.IsAny<Expression<Func<PortfolioRecalcJob, Task>>>()), Times.Once);
        metrics.Verify(m => m.RecordSuccess(85, false, It.IsAny<TimeSpan>()), Times.Once);
    }
}
