using System.IO;
using System.Text;
using System.Text.Json;
using FIBRADIS.Api.Controllers;
using FIBRADIS.Api.Diagnostics;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Application.Abstractions;
using FIBRADIS.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FIBRADIS.Tests.Unit.Controllers;

public class SecuritiesEndpointsTests
{
    [Fact]
    public async Task GetByTicker_ReturnsSingle()
    {
        var repository = new InMemorySecuritiesRepository();
        var metrics = new SecuritiesMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var cache = new InMemorySecuritiesCacheService(repository, metrics, clock);
        repository.Changed += (_, _) => cache.InvalidateCache();

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        context.Request.QueryString = new QueryString("?ticker=FUNO11");

        var result = await SecuritiesEndpoints.HandleGetSecuritiesAsync(
            context,
            cache,
            metrics,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var payload = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        var securities = JsonSerializer.Deserialize<List<SecurityDto>>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.NotNull(securities);
        var security = Assert.Single(securities);
        Assert.Equal("FUNO11", security.Ticker);
        Assert.True(context.Response.Headers.ContainsKey("ETag"));
        Assert.Equal("public, max-age=60", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task GetByTicker_NotFound()
    {
        var repository = new InMemorySecuritiesRepository();
        var metrics = new SecuritiesMetricsCollector();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        using var cache = new InMemorySecuritiesCacheService(repository, metrics, clock);
        repository.Changed += (_, _) => cache.InvalidateCache();

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        context.Request.QueryString = new QueryString("?ticker=UNKNOWN");

        var result = await SecuritiesEndpoints.HandleGetSecuritiesAsync(
            context,
            cache,
            metrics,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Contains("UNKNOWN", await ReadBodyAsync(context.Response.Body));
    }

    private static async Task<string> ReadBodyAsync(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync();
        stream.Seek(0, SeekOrigin.Begin);
        return content;
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; private set; }
    }
}
