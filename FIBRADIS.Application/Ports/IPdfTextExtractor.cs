using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IPdfTextExtractor
{
    Task<PdfTextExtraction> ExtractAsync(byte[] content, CancellationToken ct);
}
