namespace FIBRADIS.Application.Ports;

public interface IPortfolioRepository
{
    Task BeginTransactionAsync(CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    Task RollbackAsync(CancellationToken ct);

    Task DeleteUserPortfolioAsync(string userId, CancellationToken ct);

    Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct);

    Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct);
}
