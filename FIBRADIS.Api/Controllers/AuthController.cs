using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FIBRADIS.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResult>> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.AuthenticateAsync(request.Username, request.Password, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", cancellationToken).ConfigureAwait(false);
        Response.Cookies.Append("refreshToken", result.Tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenPair>> RefreshAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var token = request.RefreshToken ?? Request.Cookies["refreshToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "REFRESH_TOKEN_REQUIRED" });
        }

        var pair = await _authService.RefreshAsync(token!, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", cancellationToken).ConfigureAwait(false);
        Response.Cookies.Append("refreshToken", pair.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        });
        return Ok(pair);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> LogoutAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var token = request.RefreshToken ?? Request.Cookies["refreshToken"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            await _authService.LogoutAsync(token!, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", cancellationToken).ConfigureAwait(false);
            Response.Cookies.Delete("refreshToken");
        }

        return NoContent();
    }

    public sealed record LoginRequest([Required] string Username, [Required] string Password);

    public sealed record RefreshRequest(string? RefreshToken);
}
