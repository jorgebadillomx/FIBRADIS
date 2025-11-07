using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, UserAccount> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<string>> _roles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPasswordHasher<UserAccount> _passwordHasher;

    public InMemoryUserStore(IPasswordHasher<UserAccount> passwordHasher)
    {
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));

        Seed();
    }

    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        _users.TryGetValue(username, out var user);
        return Task.FromResult(user);
    }

    public Task<UserAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = _users.Values.FirstOrDefault(u => string.Equals(u.Id, userId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken)
    {
        if (_roles.TryGetValue(userId, out var roles))
        {
            return Task.FromResult(roles);
        }

        var user = _users.Values.FirstOrDefault(u => string.Equals(u.Id, userId, StringComparison.OrdinalIgnoreCase));
        IReadOnlyCollection<string> defaultRoles = user is null ? Array.Empty<string>() : new[] { user.Role };
        return Task.FromResult(defaultRoles);
    }

    private void Seed()
    {
        var admin = new UserAccount
        {
            Id = "admin-1",
            Username = "admin",
            Role = "admin",
            PasswordHash = string.Empty
        };
        admin = admin with { PasswordHash = _passwordHasher.HashPassword(admin, "Admin123!") };
        _users[admin.Username] = admin;
        _roles[admin.Id] = new[] { "admin" };

        var user = new UserAccount
        {
            Id = "user-1",
            Username = "user",
            Role = "user",
            PasswordHash = string.Empty
        };
        user = user with { PasswordHash = _passwordHasher.HashPassword(user, "User123!") };
        _users[user.Username] = user;
        _roles[user.Id] = new[] { "user" };

        var viewer = new UserAccount
        {
            Id = "viewer-1",
            Username = "viewer",
            Role = "viewer",
            PasswordHash = string.Empty
        };
        viewer = viewer with { PasswordHash = _passwordHasher.HashPassword(viewer, "Viewer123!") };
        _users[viewer.Username] = viewer;
        _roles[viewer.Id] = new[] { "viewer" };
    }
}
