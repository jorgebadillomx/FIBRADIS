using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface ISecuritiesCacheService
{
    Task<SecuritiesCacheResult> GetCachedAsync(CancellationToken ct);

    void InvalidateCache();
}

public sealed record SecuritiesCacheResult(
    IReadOnlyList<SecurityDto> Securities,
    bool FromCache,
    string ETag,
    string JsonPayload);
