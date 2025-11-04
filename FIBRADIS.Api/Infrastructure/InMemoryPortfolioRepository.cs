using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FIBRADIS.Application.Ports;

namespace FIBRADIS.Api.Infrastructure;

public sealed class InMemoryPortfolioRepository : IPortfolioRepository
{
    private readonly ConcurrentDictionary<string, List<(string ticker, decimal qty, decimal avgCost)>> _store =
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
