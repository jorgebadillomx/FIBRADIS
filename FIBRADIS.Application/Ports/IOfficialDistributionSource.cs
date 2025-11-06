using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IOfficialDistributionSource
{
    Task<IReadOnlyList<OfficialDistributionRecord>> GetOfficialDistributionsAsync(string ticker, DateTime payDate, CancellationToken ct);
}
