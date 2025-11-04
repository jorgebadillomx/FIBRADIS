using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using FIBRADIS.Application.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FIBRADIS.Tests.Unit.Services;

public sealed class PortfolioReplaceServiceTests
{
    private static PortfolioReplaceService CreateService(
        Mock<IPortfolioRepository> repository,
        Mock<ISecurityCatalog> securityCatalog,
        Mock<IDistributionReader> distributionReader,
        Mock<IJobScheduler> jobScheduler,
        Mock<IClock>? clock = null,
        Mock<ICorrelationIdAccessor>? correlationIdAccessor = null,
        Mock<ILogger<PortfolioReplaceService>>? logger = null)
    {
        clock ??= new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

        correlationIdAccessor ??= new Mock<ICorrelationIdAccessor>();
        correlationIdAccessor.SetupGet(accessor => accessor.CorrelationId).Returns("corr-123");

        logger ??= new Mock<ILogger<PortfolioReplaceService>>();

        return new PortfolioReplaceService(
            repository.Object,
            securityCatalog.Object,
            distributionReader.Object,
            jobScheduler.Object,
            clock.Object,
            correlationIdAccessor.Object,
            logger.Object);
    }

    [Fact]
    public async Task ReplaceAsync_WhenPortfolioIsValid_ReplacesAndReturnsSnapshot()
    {
        // Arrange
        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 10, AvgCost = 100 },
            new NormalizedRow { Ticker = "FIBRAMQ12", Qty = 5, AvgCost = 200 }
        };
        var parserIssues = new[]
        {
            new ValidationIssue { Severity = "Warning", Message = "ignored" }
        };

        var repository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        repository.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.DeleteUserPortfolioAsync("user-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.InsertTradesAsync("user-1", It.IsAny<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        repository.Setup(r => r.GetMaterializedPositionsAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string ticker, decimal qty, decimal avgCost)>
            {
                ("FUNO11", 10m, 100m),
                ("FIBRAMQ12", 5m, 200m)
            });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var securityCatalog = new Mock<ISecurityCatalog>(MockBehavior.Strict);
        securityCatalog.Setup(c => c.GetLastPricesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNO11"] = 110m,
                ["FIBRAMQ12"] = 180m
            });

        var distributionReader = new Mock<IDistributionReader>(MockBehavior.Strict);
        distributionReader.Setup(d => d.GetYieldsAsync("FUNO11", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.05m, 0.06m));
        distributionReader.Setup(d => d.GetYieldsAsync("FIBRAMQ12", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.04m, 0.05m));

        var jobScheduler = new Mock<IJobScheduler>(MockBehavior.Strict);
        jobScheduler.Setup(scheduler => scheduler.EnqueuePortfolioRecalc("user-1", "upload", It.IsAny<DateTimeOffset>()));

        var service = CreateService(repository, securityCatalog, distributionReader, jobScheduler);

        // Act
        var response = await service.ReplaceAsync("user-1", rows, parserIssues, CancellationToken.None);

        // Assert
        repository.Verify(r => r.InsertTradesAsync(
            "user-1",
            It.Is<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(
                trades => trades.Count() == 2 &&
                          trades.Any(trade => trade.ticker == "FUNO11" && trade.qty == 10m && trade.avgCost == 100m) &&
                          trades.Any(trade => trade.ticker == "FIBRAMQ12" && trade.qty == 5m && trade.avgCost == 200m)),
            It.IsAny<CancellationToken>()), Times.Once);

        var expectedTimestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        jobScheduler.Verify(scheduler => scheduler.EnqueuePortfolioRecalc("user-1", "upload", expectedTimestamp), Times.Once);

        Assert.Equal(2, response.Positions.Count);
        Assert.Equal(2, response.Imported);
        Assert.Equal(0, response.Ignored);
        Assert.Equal("corr-123", response.RequestId);

        var first = response.Positions[0];
        Assert.Equal("FUNO11", first.Ticker);
        Assert.Equal(1100m, first.Value);
        Assert.Equal(0.55m, first.Weight);
        Assert.Equal(0.05m, first.YieldTtm);
        Assert.Equal(0.06m, first.YieldForward);

        var metrics = response.Metrics;
        Assert.Equal(2000m, metrics.Invested);
        Assert.Equal(2000m, metrics.Value);
        Assert.Equal(0m, metrics.Pnl);
        Assert.Equal(0.0455m, metrics.YieldTtm);
        Assert.Equal(0.055m, metrics.YieldForward);
    }

    [Fact]
    public async Task ReplaceAsync_WhenPriceMissing_SetsZeroValueAndNegativePnl()
    {
        // Arrange
        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 3, AvgCost = 120 }
        };

        var repository = new Mock<IPortfolioRepository>();
        repository.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.DeleteUserPortfolioAsync("user-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.InsertTradesAsync("user-1", It.IsAny<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.GetMaterializedPositionsAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string ticker, decimal qty, decimal avgCost)>
            {
                ("FUNO11", 3m, 120m)
            });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var securityCatalog = new Mock<ISecurityCatalog>();
        securityCatalog.Setup(c => c.GetLastPricesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());
        securityCatalog.Setup(c => c.GetLastPriceAsync("FUNO11", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        var distributionReader = new Mock<IDistributionReader>();
        distributionReader.Setup(d => d.GetYieldsAsync("FUNO11", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.05m, 0.05m));

        var jobScheduler = new Mock<IJobScheduler>();
        jobScheduler.Setup(scheduler => scheduler.EnqueuePortfolioRecalc("user-1", "upload", It.IsAny<DateTimeOffset>()));

        var service = CreateService(repository, securityCatalog, distributionReader, jobScheduler);

        // Act
        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        // Assert
        var position = Assert.Single(response.Positions);
        Assert.Equal(0m, position.Value);
        Assert.Equal(-360m, position.Pnl);
        Assert.Equal(0m, position.Weight);
        Assert.Null(response.Metrics.YieldTtm);
        Assert.Null(response.Metrics.YieldForward);
    }

    [Fact]
    public async Task ReplaceAsync_WhenYieldsMissing_ComputesTotalsFromAvailableValues()
    {
        // Arrange
        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 2, AvgCost = 100 },
            new NormalizedRow { Ticker = "FIBRAMQ12", Qty = 1, AvgCost = 200 }
        };

        var repository = new Mock<IPortfolioRepository>();
        repository.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.DeleteUserPortfolioAsync("user-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.InsertTradesAsync("user-1", It.IsAny<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(r => r.GetMaterializedPositionsAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string ticker, decimal qty, decimal avgCost)>
            {
                ("FUNO11", 2m, 100m),
                ("FIBRAMQ12", 1m, 200m)
            });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var securityCatalog = new Mock<ISecurityCatalog>();
        securityCatalog.Setup(c => c.GetLastPricesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNO11"] = 150m,
                ["FIBRAMQ12"] = 0m
            });

        var distributionReader = new Mock<IDistributionReader>();
        distributionReader.Setup(d => d.GetYieldsAsync("FUNO11", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.10m, null));
        distributionReader.Setup(d => d.GetYieldsAsync("FIBRAMQ12", It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, null));

        var jobScheduler = new Mock<IJobScheduler>();
        jobScheduler.Setup(scheduler => scheduler.EnqueuePortfolioRecalc("user-1", "upload", It.IsAny<DateTimeOffset>()));

        var service = CreateService(repository, securityCatalog, distributionReader, jobScheduler);

        // Act
        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        // Assert
        var first = response.Positions.First(position => position.Ticker == "FUNO11");
        Assert.Equal(300m, first.Value);
        Assert.Equal(0.10m, first.YieldTtm);
        Assert.Null(first.YieldForward);

        var second = response.Positions.First(position => position.Ticker == "FIBRAMQ12");
        Assert.Equal(0m, second.Value);
        Assert.Null(second.YieldTtm);

        Assert.Equal(300m, response.Metrics.Value);
        Assert.Equal(400m, response.Metrics.Invested);
        Assert.Equal(-100m, response.Metrics.Pnl);
        Assert.Equal(0.10m, response.Metrics.YieldTtm);
        Assert.Null(response.Metrics.YieldForward);
    }

    [Fact]
    public async Task ReplaceAsync_WhenInsertFails_RollsBackAndThrows()
    {
        // Arrange
        var rows = new[]
        {
            new NormalizedRow { Ticker = "FUNO11", Qty = 1, AvgCost = 100 }
        };

        var repository = new Mock<IPortfolioRepository>();
        repository.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.DeleteUserPortfolioAsync("user-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.InsertTradesAsync("user-1", It.IsAny<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        repository.Setup(r => r.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask).Verifiable();

        var securityCatalog = new Mock<ISecurityCatalog>();
        var distributionReader = new Mock<IDistributionReader>();
        var jobScheduler = new Mock<IJobScheduler>();

        var service = CreateService(repository, securityCatalog, distributionReader, jobScheduler);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None));

        repository.Verify(r => r.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        jobScheduler.Verify(scheduler => scheduler.EnqueuePortfolioRecalc(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()), Times.Never);
    }

    [Fact]
    public async Task ReplaceAsync_WhenDuplicatesRemain_AggregatesByTicker()
    {
        // Arrange
        var rows = new[]
        {
            new NormalizedRow { Ticker = "funo11 ", Qty = 1, AvgCost = 100 },
            new NormalizedRow { Ticker = "FUNO11", Qty = 2, AvgCost = 200 }
        };

        var repository = new Mock<IPortfolioRepository>();
        repository.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.DeleteUserPortfolioAsync("user-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.InsertTradesAsync("user-1", It.IsAny<IEnumerable<(string ticker, decimal qty, decimal avgCost)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<string, IEnumerable<(string ticker, decimal qty, decimal avgCost)>, CancellationToken>((_, trades, _) =>
            {
                var trade = Assert.Single(trades);
                Assert.Equal("FUNO11", trade.ticker);
                Assert.Equal(3m, trade.qty);
                Assert.Equal(166.666667m, trade.avgCost);
            });
        repository.Setup(r => r.GetMaterializedPositionsAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string ticker, decimal qty, decimal avgCost)>
            {
                ("FUNO11", 3m, 166.666666m)
            });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var securityCatalog = new Mock<ISecurityCatalog>();
        securityCatalog.Setup(c => c.GetLastPricesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNO11"] = 170m
            });

        var distributionReader = new Mock<IDistributionReader>();
        distributionReader.Setup(d => d.GetYieldsAsync("FUNO11", It.IsAny<CancellationToken>()))
            .ReturnsAsync((0.05m, 0.05m));

        var jobScheduler = new Mock<IJobScheduler>();
        jobScheduler.Setup(scheduler => scheduler.EnqueuePortfolioRecalc("user-1", "upload", It.IsAny<DateTimeOffset>()));

        var service = CreateService(repository, securityCatalog, distributionReader, jobScheduler);

        // Act
        var response = await service.ReplaceAsync("user-1", rows, Array.Empty<ValidationIssue>(), CancellationToken.None);

        // Assert
        var position = Assert.Single(response.Positions);
        Assert.Equal("FUNO11", position.Ticker);
        Assert.Equal(3m, position.Qty);
        Assert.Equal(166.666667m, position.AvgCost);
    }
}
