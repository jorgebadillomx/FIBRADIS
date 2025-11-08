using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace FIBRADIS.Application.Services.Auth;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUserStore _userStore;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditService _auditService;
    private readonly ISecurityMetricsRecorder _metrics;
    private readonly IPasswordHasher<UserAccount> _passwordHasher;

    public AuthService(
        IUserStore userStore,
        IRefreshTokenStore refreshTokenStore,
        IJwtTokenService jwtTokenService,
        IAuditService auditService,
        ISecurityMetricsRecorder metrics,
        IPasswordHasher<UserAccount> passwordHasher)
    {
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
        _refreshTokenStore = refreshTokenStore ?? throw new ArgumentNullException(nameof(refreshTokenStore));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    public async Task<AuthResult> AuthenticateAsync(string username, string password, string ipAddress, CancellationToken cancellationToken)
    {
        var user = await _userStore.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            await RegisterFailureAsync(user?.Id ?? string.Empty, "auth.login", ipAddress, "user_not_found", cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            await RegisterFailureAsync(user.Id, "auth.login", ipAddress, "invalid_password", cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Invalid credentials");
        }

        var accessToken = _jwtTokenService.CreateAccessToken(user.Id, user.Username, user.Role);
        var refreshToken = GenerateRefreshToken(user.Id, ipAddress);

        await _refreshTokenStore.SaveAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        var roles = await _userStore.GetRolesAsync(user.Id, cancellationToken).ConfigureAwait(false);
        await _userStore.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);

        _metrics.RecordAuthLogin();
        await _auditService.RecordAsync(new AuditEntry(
            user.Id,
            "auth.login",
            "success",
            ipAddress,
            new Dictionary<string, object?>
            {
                ["refreshTokenExpiresAt"] = refreshToken.ExpiresAtUtc,
                ["roles"] = roles
            }), cancellationToken).ConfigureAwait(false);

        return new AuthResult(new TokenPair(accessToken, refreshToken.Token, (long)AccessTokenLifetime.TotalSeconds), roles);
    }

    public async Task<TokenPair> RefreshAsync(string refreshTokenValue, string ipAddress, CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenStore.FindAsync(refreshTokenValue, cancellationToken).ConfigureAwait(false);
        if (refreshToken is null)
        {
            _metrics.RecordAuthFailed();
            throw new UnauthorizedAccessException("Invalid refresh token");
        }

        if (refreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await _refreshTokenStore.RevokeAsync(refreshToken.Token, cancellationToken).ConfigureAwait(false);
            _metrics.RecordAuthFailed();
            throw new UnauthorizedAccessException("Expired refresh token");
        }

        if (refreshToken.IsRevoked)
        {
            _metrics.RecordAuthFailed();
            throw new UnauthorizedAccessException("Revoked refresh token");
        }

        var user = await _userStore.FindByIdAsync(refreshToken.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            await _refreshTokenStore.RevokeAsync(refreshToken.Token, cancellationToken).ConfigureAwait(false);
            _metrics.RecordAuthFailed();
            throw new UnauthorizedAccessException("User inactive");
        }

        var newAccessToken = _jwtTokenService.CreateAccessToken(user.Id, user.Username, user.Role);
        var newRefreshToken = GenerateRefreshToken(user.Id, ipAddress);
        await _refreshTokenStore.RotateAsync(refreshToken.Token, newRefreshToken, cancellationToken).ConfigureAwait(false);

        var roles = await _userStore.GetRolesAsync(user.Id, cancellationToken).ConfigureAwait(false);
        _metrics.RecordAuthRefresh();
        await _auditService.RecordAsync(new AuditEntry(
            user.Id,
            "auth.refresh",
            "success",
            ipAddress,
            new Dictionary<string, object?>
            {
                ["previousToken"] = refreshToken.Token,
                ["newTokenExpiresAt"] = newRefreshToken.ExpiresAtUtc
            }), cancellationToken).ConfigureAwait(false);

        return new TokenPair(newAccessToken, newRefreshToken.Token, (long)AccessTokenLifetime.TotalSeconds);
    }

    public async Task LogoutAsync(string refreshTokenValue, string ipAddress, CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenStore.FindAsync(refreshTokenValue, cancellationToken).ConfigureAwait(false);
        if (refreshToken is null)
        {
            return;
        }

        await _refreshTokenStore.RevokeAsync(refreshTokenValue, cancellationToken).ConfigureAwait(false);
        await _auditService.RecordAsync(new AuditEntry(
            refreshToken.UserId,
            "auth.logout",
            "success",
            ipAddress,
            new Dictionary<string, object?>
            {
                ["refreshToken"] = refreshTokenValue
            }), cancellationToken).ConfigureAwait(false);
    }

    public ClaimsPrincipal ValidateAccessToken(string token)
    {
        return _jwtTokenService.ValidateToken(token);
    }

    private RefreshToken GenerateRefreshToken(string userId, string ipAddress)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        return new RefreshToken
        {
            Token = token,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(RefreshTokenLifetime),
            CreatedByIp = ipAddress
        };
    }

    private async Task RegisterFailureAsync(string userId, string action, string ipAddress, string reason, CancellationToken cancellationToken)
    {
        _metrics.RecordAuthFailed();
        await _auditService.RecordAsync(new AuditEntry(
            userId,
            action,
            "failure",
            ipAddress,
            new Dictionary<string, object?>
            {
                ["reason"] = reason
            }), cancellationToken).ConfigureAwait(false);
    }
}
