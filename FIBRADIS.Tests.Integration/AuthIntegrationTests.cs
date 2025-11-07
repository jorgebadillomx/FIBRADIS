using System.Collections.Generic;
using System.Net.Http.Json;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FIBRADIS.Tests.Integration;

public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task LoginRefreshLogout_Flow_Works()
    {
        using var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest("user", "User123!"));
        login.EnsureSuccessStatusCode();
        var authResult = await login.Content.ReadFromJsonAsync<AuthEnvelope>();
        Assert.NotNull(authResult);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResult!.Tokens.AccessToken);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(authResult.Tokens.RefreshToken));
        refresh.EnsureSuccessStatusCode();
        var refreshed = await refresh.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(refreshed);
        Assert.NotEqual(authResult.Tokens.RefreshToken, refreshed!.RefreshToken);

        var logout = await client.PostAsJsonAsync("/auth/logout", new RefreshRequest(refreshed.RefreshToken));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, logout.StatusCode);

        var failedRefresh = await client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(authResult.Tokens.RefreshToken));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, failedRefresh.StatusCode);
    }

    [Fact]
    public async Task AuthorizedUser_CanAccessProtectedEndpoint()
    {
        using var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest("viewer", "Viewer123!"));
        login.EnsureSuccessStatusCode();
        var authResult = await login.Content.ReadFromJsonAsync<AuthEnvelope>();
        Assert.NotNull(authResult);

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authResult!.Tokens.AccessToken);

        var response = await client.GetAsync("/v1/securities");
        response.EnsureSuccessStatusCode();
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record RefreshRequest(string? RefreshToken);
    private sealed record AuthEnvelope(TokenPair Tokens, IReadOnlyCollection<string> Roles);
}
