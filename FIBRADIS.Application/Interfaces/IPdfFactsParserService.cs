using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface IPdfFactsParserService
{
    Task<ParsedFactsResult> ParseAsync(ParseFactsRequest request, CancellationToken ct = default);
}
