using System;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Interfaces.Admin;
using FIBRADIS.Application.Models.Admin;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemorySystemSettingsStore : ISystemSettingsStore
{
    private SystemSettings _settings = new()
    {
        MarketHoursMx = "08:30-15:00",
        LlmMaxTokensPerUser = 100_000,
        SecurityMaxUploadSize = 2 * 1024 * 1024,
        SystemMaintenanceMode = false
    };

    public Task<SystemSettings> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_settings);
    }

    public Task<SystemSettings> UpdateAsync(SystemSettingsUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var current = _settings;
        var updated = new SystemSettings
        {
            MarketHoursMx = update.MarketHoursMx ?? current.MarketHoursMx,
            LlmMaxTokensPerUser = update.LlmMaxTokensPerUser ?? current.LlmMaxTokensPerUser,
            SecurityMaxUploadSize = update.SecurityMaxUploadSize ?? current.SecurityMaxUploadSize,
            SystemMaintenanceMode = update.SystemMaintenanceMode ?? current.SystemMaintenanceMode
        };

        _settings = updated;
        return Task.FromResult(updated);
    }
}
