using System.Linq;

namespace FIBRADIS.Api.Tests.Integration;

public class MetricsEndpointTests : IClassFixture<ApiApplicationFactory>
{
    private readonly HttpClient _client;

    public MetricsEndpointTests(ApiApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExposesRequestDurationMetrics()
    {
        var pingResponse = await _client.GetAsync("/v1/ping");
        pingResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync("/metrics");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0.0.4", response.Content.Headers.ContentType?.Parameters.FirstOrDefault(p => p.Name == "version")?.Value);

        var content = await response.Content.ReadAsStringAsync();

        Assert.Contains("# HELP http_requests_total", content);
        Assert.Contains("http_requests_total{method=\"GET\",path=\"/v1/ping\",status_code=\"200\"}", content);
    }
}
