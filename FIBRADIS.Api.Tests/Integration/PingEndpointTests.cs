
namespace FIBRADIS.Api.Tests.Integration;

public class PingEndpointTests : IClassFixture<ApiApplicationFactory>
{
    private readonly HttpClient _client;

    public PingEndpointTests(ApiApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ReturnsPongWithGeneratedRequestId()
    {
        var response = await _client.GetAsync("/v1/ping");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var requestIdHeader));
        var payload = await response.Content.ReadFromJsonAsync<PingResponseContract>();

        Assert.NotNull(payload);
        Assert.Equal("pong", payload!.Message);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestId));
        Assert.Equal(payload.RequestId, Assert.Single(requestIdHeader));
    }

    [Fact]
    public async Task ReusesRequestIdProvidedByClient()
    {
        const string requestId = "fixed-request-id";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/ping");
        request.Headers.Add("X-Request-Id", requestId);

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PingResponseContract>();

        Assert.NotNull(payload);
        Assert.Equal(requestId, payload!.RequestId);
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var responseHeader));
        Assert.Equal(requestId, Assert.Single(responseHeader));
    }

    private sealed record PingResponseContract(string Message, string RequestId);
}
