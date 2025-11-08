using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Interfaces;

public interface INewsCuratorService
{
    Task<IReadOnlyList<NewsRecord>> GetPendingAsync(CancellationToken cancellationToken);
    Task<NewsRecord> ApproveAsync(Guid newsId, NewsCuratorContext context, CancellationToken cancellationToken);
    Task<NewsRecord> IgnoreAsync(Guid newsId, NewsCuratorContext context, CancellationToken cancellationToken);
    Task<NewsRecord> UpdateAsync(Guid newsId, NewsUpdate update, NewsCuratorContext context, CancellationToken cancellationToken);
}
