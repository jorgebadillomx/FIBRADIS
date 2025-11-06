using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface IDistributionReconcilerService
{
    Task<DistributionReconcileSummary> ReconcileAsync(CancellationToken ct);
}
