using System;
using System.Collections.Generic;
using System.Linq;
using FIBRADIS.Api.Security;
using FIBRADIS.Application.Interfaces.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FIBRADIS.Tests.Unit.Services.Auth;

public sealed class SecurityServicesTests
{
    private sealed class StubMetricsRecorder : ISecurityMetricsRecorder
    {
        public int Blocks { get; private set; }
        public void RecordAuthLogin() { }
        public void RecordAuthRefresh() { }
        public void RecordAuthFailed() { }
        public void RecordRateLimitBlocked() => Blocks++;
        public void RecordByokKeyActive() { }
        public void RecordByokUsage(int tokens) { }
    }

    [Fact]
    public async Task SecretService_EncryptsAndDecryptsKeys()
    {
        var options = Options.Create(new SecretEncryptionOptions
        {
            MasterKey = "0123456789ABCDEF0123456789ABCDEF"
        });
        var metrics = new StubMetricsRecorder();
        var service = new AesSecretService(options, metrics);

        await service.StoreAsync("user-1", "openai", "sk-test-123", CancellationToken.None);
        var decrypted = await service.RetrieveAsync("user-1", "openai", CancellationToken.None);

        Assert.Equal("sk-test-123", decrypted);
    }

    [Fact]
    public void RateLimiter_BlocksWhenLimitExceeded()
    {
        var metrics = new StubMetricsRecorder();
        var service = new MemoryRateLimiterService(metrics);
        var key = "user:role";

        for (var i = 0; i < 5; i++)
        {
            Assert.True(service.TryConsumeAsync(key, TimeSpan.FromMinutes(1), 5, 1, CancellationToken.None).GetAwaiter().GetResult());
        }

        var allowed = service.TryConsumeAsync(key, TimeSpan.FromMinutes(1), 5, 1, CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(allowed);
        Assert.Equal(1, metrics.Blocks);
    }

    [Fact]
    public async Task AuditService_StoresLogs()
    {
        var service = new InMemoryAuditService(NullLogger<InMemoryAuditService>.Instance);
        var entry = new AuditEntry("user-1", "auth.login", "success", "127.0.0.1", new Dictionary<string, object?>());

        await service.RecordAsync(entry, CancellationToken.None);

        Assert.Single(service.GetLogs());
        Assert.Equal("auth.login", service.GetLogs().First().Action);
    }
}
