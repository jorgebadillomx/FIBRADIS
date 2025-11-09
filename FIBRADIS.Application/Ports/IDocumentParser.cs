using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IDocumentParser
{
    Task<ParseResult> ParseAsync(DocumentParseRequest request, CancellationToken ct);
}
