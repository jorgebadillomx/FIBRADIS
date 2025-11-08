using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class NewsCuratorServiceTests
{
    [Fact]
    public async Task ApproveAsync_ChangesStatusAndLogsAudit()
    {
        var repository = new InMemoryNewsRepository();
        var auditService = new Mock<IAuditService>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 3, 3, 10, 0, 0, TimeSpan.Zero));
        var logger = new Mock<ILogger<NewsCuratorService>>();

        var news = new NewsRecord
        {
            Title = "FUNO expande portafolio",
            Summary = "La FIBRA anunció expansión",
            Url = "https://example.com/news",
            UrlHash = "hash",
            PublishedAt = clock.Object.UtcNow,
            Source = "El Economista",
            Status = NewsStatus.Pending,
            CreatedAt = clock.Object.UtcNow,
            CreatedBy = "system"
        };
        await repository.SaveAsync(news, CancellationToken.None);

        var service = new NewsCuratorService(repository, auditService.Object, clock.Object, logger.Object);

        var context = new NewsCuratorContext("admin-1", "admin", "127.0.0.1");
        var updated = await service.ApproveAsync(news.Id, context, CancellationToken.None);

        Assert.Equal(NewsStatus.Published, updated.Status);
        Assert.Equal("admin-1", updated.UpdatedBy);
        auditService.Verify(a => a.RecordAsync(It.Is<AuditEntry>(entry => entry.Action == "news.curated" && (string)entry.Metadata["status"]! == "Published"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
