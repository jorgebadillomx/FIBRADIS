using FIBRADIS.Application.Models.Documents;

namespace FIBRADIS.Application.Ports;

public interface IDocumentDownloader
{
    Task<DownloadResult> DownloadAsync(DocumentDownloadRequest request, CancellationToken ct);
}
