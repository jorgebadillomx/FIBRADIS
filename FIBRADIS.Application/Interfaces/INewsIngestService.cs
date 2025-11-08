using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface INewsIngestService
{
    Task<NewsIngestionResult> IngestAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
