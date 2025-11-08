using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models.Admin;

namespace FIBRADIS.Application.Interfaces.Admin;

public interface ISystemSettingsStore
{
    Task<SystemSettings> GetAsync(CancellationToken cancellationToken);
    Task<SystemSettings> UpdateAsync(SystemSettingsUpdate update, CancellationToken cancellationToken);
}
