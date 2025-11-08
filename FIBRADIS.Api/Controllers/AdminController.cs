using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Models.Admin;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FIBRADIS.Api.Controllers;

[ApiController]
[Route("v1/admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(PaginatedResponse<AdminUserResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsersAsync([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetUsersAsync(search, page, pageSize, cancellationToken).ConfigureAwait(false);
        var response = new PaginatedResponse<AdminUserResponse>(
            result.Items.Select(AdminUserResponse.FromModel).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
        return Ok(response);
    }

    [HttpPost("users")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateUserAsync([FromBody] CreateAdminUserRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var context = CreateAdminContext();
        var created = await _adminService.CreateUserAsync(
            new AdminUserCreateRequest(request.Email, request.Role, request.Password, request.IsActive),
            context,
            cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetUsersAsync), new { id = created.UserId }, AdminUserResponse.FromModel(created));
    }

    [HttpPut("users/{id}")]
    [ProducesResponseType(typeof(AdminUserResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUserAsync([FromRoute] string id, [FromBody] UpdateAdminUserRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var context = CreateAdminContext();
        var updated = await _adminService.UpdateUserAsync(
            id,
            new AdminUserUpdateRequest(request.Email, request.Role, request.IsActive, request.Password),
            context,
            cancellationToken).ConfigureAwait(false);

        return Ok(AdminUserResponse.FromModel(updated));
    }

    [HttpDelete("users/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateUserAsync([FromRoute] string id, CancellationToken cancellationToken)
    {
        var context = CreateAdminContext();
        await _adminService.DeactivateUserAsync(id, context, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(PaginatedResponse<AuditLogResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogsAsync(
        [FromQuery] string? userId,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new AuditLogQuery(userId, action, from?.ToUniversalTime(), to?.ToUniversalTime(), page, pageSize);
        var result = await _adminService.GetAuditLogsAsync(query, cancellationToken).ConfigureAwait(false);
        var response = new PaginatedResponse<AuditLogResponse>(
            result.Items.Select(AuditLogResponse.FromModel).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
        return Ok(response);
    }

    [HttpGet("settings")]
    [ProducesResponseType(typeof(SettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _adminService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(SettingsResponse.FromModel(settings));
    }

    [HttpPut("settings")]
    [ProducesResponseType(typeof(SettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettingsAsync([FromBody] UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var context = CreateAdminContext();
        var updated = await _adminService.UpdateSettingsAsync(
            new SystemSettingsUpdate(request.MarketHoursMx, request.LlmMaxTokensPerUser, request.SecurityMaxUploadSize, request.SystemMaintenanceMode),
            context,
            cancellationToken).ConfigureAwait(false);
        return Ok(SettingsResponse.FromModel(updated));
    }

    private AdminContext CreateAdminContext()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("User identifier missing from token");
        }

        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.Claims.FirstOrDefault(c => c.Type == "role")?.Value ?? string.Empty;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return new AdminContext(userId, role, ipAddress);
    }
}
