using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Application.Interfaces.Auth;
using FIBRADIS.Application.Models.Admin;
using FIBRADIS.Application.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace FIBRADIS.Api.Security;

public sealed class InMemoryUserStore : IUserStore, IAdminUserStore
{
    private readonly ConcurrentDictionary<string, UserAccount> _usersById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _usernames = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPasswordHasher<UserAccount> _passwordHasher;

    public InMemoryUserStore(IPasswordHasher<UserAccount> passwordHasher)
    {
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        Seed();
    }

    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult<UserAccount?>(null);
        }

        if (_usernames.TryGetValue(username, out var userId) && _usersById.TryGetValue(userId, out var account))
        {
            return Task.FromResult<UserAccount?>(account);
        }

        return Task.FromResult<UserAccount?>(null);
    }

    public Task<UserAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        _usersById.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task<IReadOnlyCollection<string>> GetRolesAsync(string userId, CancellationToken cancellationToken)
    {
        if (_usersById.TryGetValue(userId, out var user))
        {
            return Task.FromResult<IReadOnlyCollection<string>>(user.Roles.ToArray());
        }

        return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }

    public Task UpdateLastLoginAsync(string userId, DateTime timestampUtc, CancellationToken cancellationToken)
    {
        if (_usersById.TryGetValue(userId, out var user))
        {
            var updated = user with { LastLoginUtc = timestampUtc };
            _usersById[userId] = updated;
            _usernames[user.Username] = userId;
            _emails[updated.Email] = userId;
        }

        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<AdminUser> Users, int TotalCount)> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _usersById.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
                                     || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToAdminUser)
            .ToArray();

        return Task.FromResult<(IReadOnlyList<AdminUser>, int)>((items, total));
    }

    public Task<AdminUser?> GetByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var user = _usersById.TryGetValue(userId, out var account)
            ? ToAdminUser(account)
            : null;
        return Task.FromResult(user);
    }

    public Task<AdminUser> CreateAsync(AdminUserCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var username = request.Email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0];
        var suffix = 1;
        var candidate = username;
        while (_usernames.ContainsKey(candidate))
        {
            candidate = $"{username}{suffix++}";
        }
        username = candidate;
        if (_emails.ContainsKey(request.Email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var account = new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = username,
            Email = request.Email,
            Role = request.Role,
            IsActive = request.IsActive,
            CreatedAtUtc = request.CreatedAtUtc
        };

        account = account with { PasswordHash = _passwordHasher.HashPassword(account, request.Password) };
        _usersById[account.Id] = account;
        _usernames[account.Username] = account.Id;
        _emails[account.Email] = account.Id;

        return Task.FromResult(ToAdminUser(account));
    }

    public Task<AdminUser> UpdateAsync(string userId, AdminUserUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_usersById.TryGetValue(userId, out var existing))
        {
            throw new KeyNotFoundException($"User '{userId}' not found");
        }

        var updated = existing with
        {
            Email = request.Email,
            Role = request.Role,
            IsActive = request.IsActive
        };

        if (!string.Equals(existing.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            if (_emails.TryGetValue(request.Email, out var existingId) && !string.Equals(existingId, userId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Email already exists");
            }

            _emails.TryRemove(existing.Email, out _);
            _emails[request.Email] = userId;
            var newUsername = request.Email.Split('@', StringSplitOptions.RemoveEmptyEntries)[0];
            var suffix = 1;
            var candidate = newUsername;
            while (_usernames.ContainsKey(candidate) && !string.Equals(candidate, existing.Username, StringComparison.OrdinalIgnoreCase))
            {
                candidate = $"{newUsername}{suffix++}";
            }
            newUsername = candidate;
            _usernames.TryRemove(existing.Username, out _);
            updated = updated with { Username = newUsername };
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            updated = updated with { PasswordHash = _passwordHasher.HashPassword(updated, request.Password) };
        }

        _usersById[userId] = updated;
        _usernames[updated.Username] = userId;
        _emails[updated.Email] = userId;

        return Task.FromResult(ToAdminUser(updated));
    }

    public Task DeactivateAsync(string userId, CancellationToken cancellationToken)
    {
        if (_usersById.TryGetValue(userId, out var user))
        {
            var updated = user with { IsActive = false };
            _usersById[userId] = updated;
            _usernames[updated.Username] = userId;
        }

        return Task.CompletedTask;
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        var count = _usersById.Values.Count(u => u.IsActive);
        return Task.FromResult(count);
    }

    private static AdminUser ToAdminUser(UserAccount account)
    {
        return new AdminUser
        {
            UserId = account.Id,
            Username = account.Username,
            Email = account.Email,
            Role = account.Role,
            IsActive = account.IsActive,
            CreatedAtUtc = account.CreatedAtUtc,
            LastLoginUtc = account.LastLoginUtc
        };
    }

    private void Seed()
    {
        var admin = new UserAccount
        {
            Id = "admin-1",
            Username = "admin",
            Email = "admin@fibradis.test",
            Role = UserRoles.Admin,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            IsActive = true
        };
        admin = admin with { PasswordHash = _passwordHasher.HashPassword(admin, "Admin123!") };
        _usersById[admin.Id] = admin;
        _usernames[admin.Username] = admin.Id;
        _emails[admin.Email] = admin.Id;

        var user = new UserAccount
        {
            Id = "user-1",
            Username = "user",
            Email = "user@fibradis.test",
            Role = UserRoles.User,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-7),
            IsActive = true
        };
        user = user with { PasswordHash = _passwordHasher.HashPassword(user, "User123!") };
        _usersById[user.Id] = user;
        _usernames[user.Username] = user.Id;
        _emails[user.Email] = user.Id;

        var viewer = new UserAccount
        {
            Id = "viewer-1",
            Username = "viewer",
            Email = "viewer@fibradis.test",
            Role = UserRoles.Viewer,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            IsActive = true
        };
        viewer = viewer with { PasswordHash = _passwordHasher.HashPassword(viewer, "Viewer123!") };
        _usersById[viewer.Id] = viewer;
        _usernames[viewer.Username] = viewer.Id;
        _emails[viewer.Email] = viewer.Id;
    }
}
