using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface INewsRepository
{
    Task<NewsRecord?> GetByUrlHashAsync(string urlHash, CancellationToken cancellationToken);
    Task<NewsRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<NewsRecord>> GetPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NewsRecord>> GetPublishedAsync(string? ticker, CancellationToken cancellationToken);
    Task SaveAsync(NewsRecord record, CancellationToken cancellationToken);
    Task UpdateAsync(NewsRecord record, CancellationToken cancellationToken);
}
