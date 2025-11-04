using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FIBRADIS.Api.Authentication;

public sealed class FakeJwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";

    public FakeJwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));
        }

        var rawHeader = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(rawHeader) || !rawHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization header."));
        }

        var token = rawHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Token is empty."));
        }

        var (userId, role) = ParseToken(token);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Token does not contain a valid subject."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId)
        };

        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static (string? UserId, string? Role) ParseToken(string token)
    {
        var userId = default(string?);
        var role = default(string?);

        if (token.Contains(':', StringComparison.Ordinal))
        {
            var segments = token.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                var parts = segment.Split(':', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0];
                var value = parts[1];
                if (key.Equals("sub", StringComparison.OrdinalIgnoreCase) || key.Equals("user", StringComparison.OrdinalIgnoreCase))
                {
                    userId = value;
                }
                else if (key.Equals("role", StringComparison.OrdinalIgnoreCase))
                {
                    role = value;
                }
            }
        }
        else
        {
            userId = token;
        }

        return (userId, role);
    }
}
