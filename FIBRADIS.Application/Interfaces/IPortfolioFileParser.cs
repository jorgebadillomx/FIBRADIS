using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface IPortfolioFileParser
{
    Task<(IEnumerable<NormalizedRow> Rows, IEnumerable<ValidationIssue> Issues)> ParseAsync(
        Stream fileStream,
        string fileName,
        CancellationToken ct = default);
}
