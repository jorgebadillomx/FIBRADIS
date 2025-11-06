using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IDistributionRepository
{
    Task<IReadOnlyList<string>> GetActiveFibraTickersAsync(CancellationToken ct);

    Task<bool> ExistsAsync(string ticker, DateTime payDate, decimal grossPerCbfi, CancellationToken ct);

    Task InsertAsync(DistributionRecord record, CancellationToken ct);

    Task UpdateAsync(DistributionRecord record, CancellationToken ct);

    Task<IReadOnlyList<DistributionRecord>> GetByStatusAsync(string status, CancellationToken ct);

    Task<IReadOnlyList<DistributionRecord>> GetVerifiedSinceAsync(string ticker, DateTime fromInclusive, CancellationToken ct);
}
