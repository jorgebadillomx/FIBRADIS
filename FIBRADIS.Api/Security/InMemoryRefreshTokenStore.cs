using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshToken> _tokens = new(StringComparer.Ordinal);

    public Task SaveAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
    {
        _tokens[refreshToken.Token] = refreshToken;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindAsync(string token, CancellationToken cancellationToken)
    {
        _tokens.TryGetValue(token, out var refreshToken);
        return Task.FromResult(refreshToken);
    }

    public Task<bool> IsValidAsync(string token, CancellationToken cancellationToken)
    {
        var isValid = _tokens.TryGetValue(token, out var refreshToken) && refreshToken is { IsRevoked: false } && refreshToken.ExpiresAtUtc > DateTime.UtcNow;
        return Task.FromResult(isValid);
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(token, out var refreshToken))
        {
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAtUtc = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task RotateAsync(string oldToken, RefreshToken newToken, CancellationToken cancellationToken)
    {
        if (_tokens.TryGetValue(oldToken, out var existing))
        {
            existing.IsRevoked = true;
            existing.RevokedAtUtc = DateTime.UtcNow;
            existing.ReplacedByToken = newToken.Token;
        }

        _tokens[newToken.Token] = newToken;
        return Task.CompletedTask;
    }
}
