using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface INewsSourceClient
{
    Task<IReadOnlyList<ExternalNewsArticle>> FetchAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
