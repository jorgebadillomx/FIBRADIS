using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using FIBRADIS.Application.Interfaces;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FIBRADIS.Tests.Integration;

public class SecuritiesApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SecuritiesApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    private static async Task<string> AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest("viewer", "Viewer123!"));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthEnvelope>();
        return auth!.Tokens.AccessToken;
    }

    [Fact]
    public async Task GetAllSecurities_ReturnsList()
    {
        using var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/v1/securities");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        var securities = JsonSerializer.Deserialize<List<SecurityDto>>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(securities);
        Assert.True(securities!.Count >= 10);
        Assert.True(response.Headers.ETag is not null);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task GetSecurities_ByTicker_ReturnsSingle()
    {
        using var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/v1/securities?ticker=FUNO11");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        var securities = JsonSerializer.Deserialize<List<SecurityDto>>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(securities);
        var security = Assert.Single(securities!);
        Assert.Equal("FUNO11", security.Ticker);
    }

    [Fact]
    public async Task Cache_Ttl_ReturnsSameEtagWithinWindow()
    {
        using var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var first = await client.GetAsync("/v1/securities");
        first.EnsureSuccessStatusCode();
        var second = await client.GetAsync("/v1/securities");
        second.EnsureSuccessStatusCode();

        Assert.Equal(first.Headers.ETag?.Tag, second.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Updates_Propagate_ToResponse()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecuritiesRepository>();
        var cache = scope.ServiceProvider.GetRequiredService<ISecuritiesCacheService>();

        var initial = await cache.GetCachedAsync(CancellationToken.None);
        await repository.UpdateMetricsAsync(
            "FUNO11",
            new SecurityMetricsDto
            {
                LastPrice = 45.67m,
                YieldForward = 0.12m,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        using var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/v1/securities?ticker=FUNO11");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        var securities = JsonSerializer.Deserialize<List<SecurityDto>>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(securities);
        var updated = Assert.Single(securities!);
        Assert.Equal(45.67m, updated.LastPrice);
        Assert.Equal(0.12m, updated.YieldForward);
        Assert.NotEqual(initial.ETag, response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Metrics_Endpoint_Includes_SecuritiesMetrics()
    {
        using var client = _factory.CreateClient();
        var token = await AuthenticateAsync(client);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        _ = await client.GetAsync("/v1/securities");
        var metricsResponse = await client.GetAsync("/metrics");
        metricsResponse.EnsureSuccessStatusCode();
        var payload = await metricsResponse.Content.ReadAsStringAsync();

        Assert.Contains("securities_cache_hits_total", payload);
        Assert.Contains("securities_latency_p95", payload);
    }
}

internal sealed record LoginRequest(string Username, string Password);

internal sealed record AuthEnvelope(TokenPair Tokens, IReadOnlyCollection<string> Roles);
