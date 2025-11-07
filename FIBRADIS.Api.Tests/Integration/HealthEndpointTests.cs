using System;
using System.Collections.Generic;

namespace FIBRADIS.Api.Tests.Integration;

public class HealthEndpointTests : IClassFixture<ApiApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(ApiApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsHealthyResponse()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<HealthResponseContract>();

        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload!.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.Uptime));
        Assert.NotNull(payload.Checks);
        Assert.NotEmpty(payload.Checks);
        Assert.Contains(payload.Checks, check => check.Name.Equals("sqlserver", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record HealthResponseContract(string Status, List<HealthEntryContract> Checks, string Uptime);

    private sealed record HealthEntryContract(string Name, string Status, string? Description, string Duration);
}
