using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Api.Infrastructure;
using FIBRADIS.Api.Security;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;
using FIBRADIS.Application.Services.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FIBRADIS.Tests.Unit.Services.Admin;

public sealed class AdminServiceTests
{
    private readonly InMemoryUserStore _userStore;
    private readonly InMemoryAuditService _auditService;
    private readonly InMemorySystemSettingsStore _settingsStore;
    private readonly Mock<IAdminMetricsRecorder> _metrics;
    private readonly AdminService _sut;

    public AdminServiceTests()
    {
        var passwordHasher = new PasswordHasher<UserAccount>();
        _userStore = new InMemoryUserStore(passwordHasher);
        _auditService = new InMemoryAuditService(new NullLogger<InMemoryAuditService>());
        _settingsStore = new InMemorySystemSettingsStore();
        _metrics = new Mock<IAdminMetricsRecorder>();
        _sut = new AdminService(_userStore, _auditService, _auditService, _settingsStore, _metrics.Object);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldCreateUserAndAudit()
    {
        var request = new AdminUserCreateRequest("nuevo@fibradis.test", UserRoles.User, "Password123!", true);

        var result = await _sut.CreateUserAsync(request, CreateAdminContext(), CancellationToken.None);

        Assert.Equal(request.Email, result.Email);
        Assert.Equal(UserRoles.User, result.Role);

        var logs = await _auditService.GetAsync(new AuditLogQuery(null, "admin.createUser", null, null, 1, 10), CancellationToken.None);
        Assert.Contains(logs.Logs, log => log.Metadata.TryGetValue("email", out var value) && Equals(value, request.Email));
        _metrics.Verify(m => m.IncrementAuditEntries(), Times.AtLeastOnce);
        _metrics.Verify(m => m.SetActiveUsers(It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldChangeRoleAndTrackAlert()
    {
        var request = new AdminUserCreateRequest("rolechange@fibradis.test", UserRoles.User, "Password123!", true);
        var created = await _sut.CreateUserAsync(request, CreateAdminContext(), CancellationToken.None);

        var updateRequest = new AdminUserUpdateRequest(created.Email, UserRoles.Admin, true, null);
        await _sut.UpdateUserAsync(created.UserId, updateRequest, CreateAdminContext(), CancellationToken.None);

        var updated = await _userStore.GetByIdAsync(created.UserId, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(UserRoles.Admin, updated!.Role);
        _metrics.Verify(m => m.RecordRoleChange(UserRoles.User, UserRoles.Admin), Times.Once);
    }

    [Fact]
    public async Task DeactivateUserAsync_ShouldSetInactive()
    {
        var request = new AdminUserCreateRequest("inactive@fibradis.test", UserRoles.Viewer, "Password123!", true);
        var created = await _sut.CreateUserAsync(request, CreateAdminContext(), CancellationToken.None);

        await _sut.DeactivateUserAsync(created.UserId, CreateAdminContext(), CancellationToken.None);

        var user = await _userStore.GetByIdAsync(created.UserId, CancellationToken.None);
        Assert.False(user!.IsActive);
        _metrics.Verify(m => m.SetActiveUsers(It.IsAny<int>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task UpdateSettingsAsync_ShouldPersistChanges()
    {
        var update = new SystemSettingsUpdate("09:00-16:00", 200000, 3145728, true);

        var result = await _sut.UpdateSettingsAsync(update, CreateAdminContext(), CancellationToken.None);

        Assert.Equal("09:00-16:00", result.MarketHoursMx);
        Assert.True(result.SystemMaintenanceMode);
        _metrics.Verify(m => m.IncrementSettingsChanges(), Times.Once);
        _metrics.Verify(m => m.NotifyMaintenanceModeEnabled(), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogsAsync_ShouldFilterByUser()
    {
        var request = new AdminUserCreateRequest("audituser@fibradis.test", UserRoles.User, "Password123!", true);
        var created = await _sut.CreateUserAsync(request, CreateAdminContext(), CancellationToken.None);

        await _sut.UpdateUserAsync(created.UserId, new AdminUserUpdateRequest(created.Email, UserRoles.User, true, null), CreateAdminContext(), CancellationToken.None);

        var logs = await _sut.GetAuditLogsAsync(new AuditLogQuery(created.UserId, null, null, null, 1, 20), CancellationToken.None);

        Assert.All(logs.Items, log => Assert.Equal(created.UserId, log.UserId));
        Assert.True(logs.TotalCount >= logs.Items.Count);
    }

    private static AdminContext CreateAdminContext() => new("admin-1", UserRoles.Admin, "127.0.0.1");
}
