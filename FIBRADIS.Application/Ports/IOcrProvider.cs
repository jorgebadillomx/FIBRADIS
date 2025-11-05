using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IOcrProvider
{
    Task<OcrExtractionResult> ExtractAsync(byte[] content, CancellationToken ct);
}
