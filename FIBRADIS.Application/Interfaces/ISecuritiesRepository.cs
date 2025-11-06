using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface ISecuritiesRepository
{
    Task<IReadOnlyList<SecurityEntity>> GetAllAsync(CancellationToken ct);

    Task<SecurityEntity?> GetByTickerAsync(string ticker, CancellationToken ct);

    Task UpdateMetricsAsync(string ticker, SecurityMetricsDto metrics, CancellationToken ct);
}
