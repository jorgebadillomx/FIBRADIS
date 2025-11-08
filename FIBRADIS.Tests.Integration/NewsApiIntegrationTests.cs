using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FIBRADIS.Tests.Integration;

public sealed class NewsApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NewsApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    [Fact]
    public async Task GetNews_ReturnsPublishedEntries()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INewsRepository>();
        await repository.SaveAsync(new NewsRecord
        {
            Title = "FUNO anuncia expansión",
            Summary = "La FIBRA incrementa su NOI",
            Url = "https://example.com/funo",
            UrlHash = Guid.NewGuid().ToString("N"),
            PublishedAt = DateTimeOffset.UtcNow,
            Source = "El Economista",
            FibraTicker = "FUNO11",
            Sector = "Industrial",
            Sentiment = NewsSentiment.Positive,
            Status = NewsStatus.Published,
            TokensUsed = 120,
            CostUsd = 0.12m,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "system"
        }, CancellationToken.None);

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/news");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<Dictionary<string, object?>>>(cancellationToken: CancellationToken.None);
        Assert.NotNull(payload);
        Assert.Single(payload!);
        Assert.Equal("FUNO anuncia expansión", payload![0]["title"]?.ToString());
    }
}
