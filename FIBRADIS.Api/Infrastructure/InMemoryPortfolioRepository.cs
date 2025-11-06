using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FIBRADIS.Application.Models;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryPortfolioRepository : IPortfolioRepository
{
    private readonly ConcurrentDictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> _store =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<PortfolioCashflow>> _cashflows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<PortfolioValuationSnapshot>> _valuations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PortfolioRecalcMetricsSnapshot> _currentMetrics =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string UserId, string Reason, DateOnly Date), PortfolioJobRunRecord> _jobRuns =
        new();
    private readonly ConcurrentDictionary<string, List<(Guid JobRunId, string Reason, PortfolioRecalcMetricsSnapshot Snapshot)>> _metricsHistory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<PortfolioJobDeadLetterRecord> _deadLetters = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (decimal? YieldTtm, decimal? YieldForward)>> _portfolioYields =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _transactionLock = new(1, 1);
    private readonly AsyncLocal<TransactionState?> _currentTransaction = new();

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        await _transactionLock.WaitAsync(ct).ConfigureAwait(false);
        if (_currentTransaction.Value is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        var snapshot = _store.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(tuple => (tuple.ticker, tuple.qty, tuple.avgCost)).ToList(),
            StringComparer.OrdinalIgnoreCase);

        _currentTransaction.Value = new TransactionState(snapshot);
    }

    public Task CommitAsync(CancellationToken ct)
    {
        var state = _currentTransaction.Value ?? throw new InvalidOperationException("No active transaction to commit.");

        _store.Clear();
        foreach (var (userId, positions) in state.WorkingCopy)
        {
            _store[userId] = positions.Select(position => position).ToList();
        }

        _currentTransaction.Value = null;
        _transactionLock.Release();
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct)
    {
        if (_currentTransaction.Value is not null)
        {
            _currentTransaction.Value = null;
            _transactionLock.Release();
        }

        return Task.CompletedTask;
    }

    public Task DeleteUserPortfolioAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var state = RequireTransaction();
        state.WorkingCopy.Remove(userId);
        return Task.CompletedTask;
    }

    public Task InsertTradesAsync(string userId, IEnumerable<(string ticker, decimal qty, decimal avgCost)> trades, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(trades);

        var state = RequireTransaction();
        var list = state.WorkingCopy.GetOrAdd(userId, static () => new List<(string ticker, decimal qty, decimal avgCost)>());
        list.AddRange(trades);
        return Task.CompletedTask;
    }

    public Task<List<(string ticker, decimal qty, decimal avgCost)>> GetMaterializedPositionsAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var state = _currentTransaction.Value;
        if (state is not null)
        {
            return Task.FromResult(state.WorkingCopy.TryGetValue(userId, out var positions)
                ? positions.Select(tuple => tuple).ToList()
                : new List<(string ticker, decimal qty, decimal avgCost)>());
        }

        if (_store.TryGetValue(userId, out var existing))
        {
            return Task.FromResult(existing.Select(tuple => tuple).ToList());
        }

        return Task.FromResult(new List<(string ticker, decimal qty, decimal avgCost)>());
    }

    public Task<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>> GetCurrentPositionsAsync(string userId, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<(string ticker, decimal qty, decimal avgCost)>>(GetCommittedPositions(userId));
    }

    public Task<IReadOnlyList<PortfolioCashflow>> GetCashflowHistoryAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var list = _cashflows.TryGetValue(userId, out var flows)
            ? flows.ToList()
            : new List<PortfolioCashflow>();
        return Task.FromResult<IReadOnlyList<PortfolioCashflow>>(list);
    }

    public Task<IReadOnlyList<PortfolioValuationSnapshot>> GetValuationHistoryAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var list = _valuations.TryGetValue(userId, out var valuations)
            ? valuations.OrderBy(snapshot => snapshot.AsOf).ToList()
            : new List<PortfolioValuationSnapshot>();
        return Task.FromResult<IReadOnlyList<PortfolioValuationSnapshot>>(list);
    }

    public Task<PortfolioJobRunRecord?> GetJobRunAsync(string userId, string reason, DateOnly executionDate, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        return Task.FromResult(_jobRuns.TryGetValue((userId, reason, executionDate), out var record) ? record : null);
    }

    public Task SaveJobRunAsync(PortfolioJobRunRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _jobRuns[(record.UserId, record.Reason, record.ExecutionDate)] = record;
        return Task.CompletedTask;
    }

    public Task SaveCurrentMetricsAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(snapshot);
        _currentMetrics[userId] = snapshot;
        return Task.CompletedTask;
    }

    public Task AppendMetricsHistoryAsync(string userId, PortfolioRecalcMetricsSnapshot snapshot, Guid jobRunId, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(reason);
        ArgumentNullException.ThrowIfNull(snapshot);

        var list = _metricsHistory.GetOrAdd(userId, static () => new List<(Guid, string, PortfolioRecalcMetricsSnapshot)>());
        list.Add((jobRunId, reason, snapshot));
        return Task.CompletedTask;
    }

    public Task RecordDeadLetterAsync(PortfolioJobDeadLetterRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _deadLetters.Add(record);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetUsersHoldingTickerAsync(string ticker, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(ticker);
        var users = _store
            .Where(pair => pair.Value.Any(position => string.Equals(position.ticker, ticker, StringComparison.OrdinalIgnoreCase) && position.qty > 0))
            .Select(pair => pair.Key)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(users);
    }

    public Task UpdatePortfolioYieldMetricsAsync(string userId, string ticker, decimal? yieldTtm, decimal? yieldForward, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(ticker);
        var userMetrics = _portfolioYields.GetOrAdd(userId, static () => new ConcurrentDictionary<string, (decimal?, decimal?)>(StringComparer.OrdinalIgnoreCase));
        userMetrics[ticker] = (yieldTtm, yieldForward);
        return Task.CompletedTask;
    }

    public List<(string ticker, decimal qty, decimal avgCost)> GetCommittedPositions(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return _store.TryGetValue(userId, out var positions)
            ? positions.Select(tuple => tuple).ToList()
            : new List<(string ticker, decimal qty, decimal avgCost)>();
    }

    public void SeedCashflows(string userId, IEnumerable<PortfolioCashflow> flows)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(flows);
        _cashflows[userId] = flows.OrderBy(flow => flow.Timestamp).ToList();
    }

    public void SeedValuations(string userId, IEnumerable<PortfolioValuationSnapshot> valuations)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(valuations);
        _valuations[userId] = valuations.OrderBy(snapshot => snapshot.AsOf).ToList();
    }

    public PortfolioRecalcMetricsSnapshot? GetCurrentMetrics(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return _currentMetrics.TryGetValue(userId, out var snapshot) ? snapshot : null;
    }

    public IReadOnlyDictionary<string, (decimal? YieldTtm, decimal? YieldForward)> GetPortfolioYields(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return _portfolioYields.TryGetValue(userId, out var metrics)
            ? metrics.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, (decimal?, decimal?)>(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<(Guid JobRunId, string Reason, PortfolioRecalcMetricsSnapshot Snapshot)> GetMetricsHistory(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return _metricsHistory.TryGetValue(userId, out var history)
            ? history.ToList()
            : Array.Empty<(Guid, string, PortfolioRecalcMetricsSnapshot)>();
    }

    public IReadOnlyCollection<PortfolioJobRunRecord> GetJobRuns()
    {
        return _jobRuns.Values.ToList();
    }

    public IReadOnlyCollection<PortfolioJobDeadLetterRecord> GetDeadLetters()
    {
        return _deadLetters.ToArray();
    }

    private TransactionState RequireTransaction()
    {
        var state = _currentTransaction.Value;
        if (state is null)
        {
            throw new InvalidOperationException("Transaction is required for this operation.");
        }

        return state;
    }

    private sealed class TransactionState
    {
        public TransactionState(Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> workingCopy)
        {
            WorkingCopy = workingCopy;
        }

        public Dictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> WorkingCopy { get; }
    }
}

internal static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, Func<TValue> factory)
        where TKey : notnull
    {
        if (dictionary.TryGetValue(key, out var existing))
        {
            return existing!;
        }

        var created = factory();
        dictionary[key] = created;
        return created;
    }
}
