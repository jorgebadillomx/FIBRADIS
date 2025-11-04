using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface IPortfolioReplaceService
{
    Task<UploadPortfolioResponse> ReplaceAsync(string userId, IEnumerable<NormalizedRow> rows, IEnumerable<ValidationIssue> issuesFromParser, CancellationToken ct = default);
}
