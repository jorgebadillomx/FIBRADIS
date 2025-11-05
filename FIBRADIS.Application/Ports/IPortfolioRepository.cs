using System;
using FIBRADIS.Application.Models;

namespace FIBRADIS.Application.Ports;

public interface IPortfolioRepository
{
    Task BeginTransactionAsync(CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    Task RollbackAsync(CancellationToken ct);

    Task DeleteUserPortfolioAsync(string userId, CancellationToken ct);

    Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct);

    Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct);

    Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct);

    Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct);

    Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct);

    Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct);

    Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct);
}
