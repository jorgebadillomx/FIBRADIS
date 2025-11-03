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
        Assert.NotNull(payload.Details);
    }

    private sealed record HealthResponseContract(string Status, Dictionary<string, HealthEntryContract> Details);

    private sealed record HealthEntryContract(string Status, string? Description);
}
