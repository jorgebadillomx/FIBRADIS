using FIBRADIS.Api.Security;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using FIBRADIS.Application.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FIBRADIS.Tests.Unit.Services.Auth;

public sealed class AuthServiceTests
{
    private sealed class StubMetricsRecorder : ISecurityMetricsRecorder
    {
        public int Logins { get; private set; }
        public int Refreshes { get; private set; }
        public int Failures { get; private set; }
        public int RateLimitBlocks { get; private set; }
        public int ByokActive { get; private set; }
        public int Tokens { get; private set; }

        public void RecordAuthLogin() => Logins++;
        public void RecordAuthRefresh() => Refreshes++;
        public void RecordAuthFailed() => Failures++;
        public void RecordRateLimitBlocked() => RateLimitBlocks++;
        public void RecordByokKeyActive() => ByokActive++;
        public void RecordByokUsage(int tokens) => Tokens += tokens;
    }

    private static (AuthService service, InMemoryRefreshTokenStore store, StubMetricsRecorder metrics) CreateService()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "1234567890ABCDEF1234567890ABCDEF"
        });
        var jwtTokenService = new JwtTokenService(jwtOptions);
        var refreshStore = new InMemoryRefreshTokenStore();
        var passwordHasher = new PasswordHasher<UserAccount>();
        var userStore = new InMemoryUserStore(passwordHasher);
        var auditService = new InMemoryAuditService(NullLogger<InMemoryAuditService>.Instance);
        var metrics = new StubMetricsRecorder();
        var authService = new AuthService(userStore, refreshStore, jwtTokenService, auditService, metrics, passwordHasher);
        return (authService, refreshStore, metrics);
    }

    [Fact]
    public async Task Login_GeneratesAccessAndRefreshTokens()
    {
        var (service, store, metrics) = CreateService();

        var result = await service.AuthenticateAsync("admin", "Admin123!", "127.0.0.1", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.Tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Tokens.RefreshToken));
        Assert.True(await store.IsValidAsync(result.Tokens.RefreshToken, CancellationToken.None));
        Assert.Contains("admin", result.Roles, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, metrics.Logins);
    }

    [Fact]
    public async Task Refresh_RotatesRefreshToken()
    {
        var (service, store, metrics) = CreateService();
        var login = await service.AuthenticateAsync("user", "User123!", "127.0.0.1", CancellationToken.None);

        var refreshed = await service.RefreshAsync(login.Tokens.RefreshToken, "127.0.0.1", CancellationToken.None);

        Assert.NotEqual(login.Tokens.RefreshToken, refreshed.RefreshToken);
        Assert.False(await store.IsValidAsync(login.Tokens.RefreshToken, CancellationToken.None));
        Assert.True(await store.IsValidAsync(refreshed.RefreshToken, CancellationToken.None));
        Assert.Equal(1, metrics.Refreshes);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (service, store, _) = CreateService();
        var login = await service.AuthenticateAsync("viewer", "Viewer123!", "127.0.0.1", CancellationToken.None);

        await service.LogoutAsync(login.Tokens.RefreshToken, "127.0.0.1", CancellationToken.None);

        Assert.False(await store.IsValidAsync(login.Tokens.RefreshToken, CancellationToken.None));
    }

    [Fact]
    public async Task Login_InvalidPassword_RegistersFailure()
    {
        var (service, _, metrics) = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AuthenticateAsync("viewer", "wrong", "0.0.0.0", CancellationToken.None));

        Assert.Equal(1, metrics.Failures);
    }
}
