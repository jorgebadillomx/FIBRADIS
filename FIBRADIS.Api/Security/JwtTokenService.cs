using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FIBRADIS.Application.Interfaces.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FIBRADIS.Api.Security;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException("JWT signing key must be provided");
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public string CreateAccessToken(string userId, string username, string role)
    {
        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(), ClaimValueTypes.Integer64)
        };

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(_options.AccessTokenLifetime),
            signingCredentials: _signingCredentials);

        return _tokenHandler.WriteToken(jwt);
    }

    public ClaimsPrincipal ValidateToken(string token)
    {
        var principal = _tokenHandler.ValidateToken(token, _validationParameters, out _);
        return principal;
    }
}
