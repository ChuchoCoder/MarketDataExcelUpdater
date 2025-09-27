using MarketDataExcelUpdater.Core.Configuration;

namespace MarketDataExcelUpdater.Core.Retention;

/// <summary>
/// Maintains bounded per-symbol tick metadata (sequence + timestamp) and enforces
/// both count-based and age-based retention policies.
/// </summary>
public sealed class TickRetentionManager : ITickRetentionManager
{
    private readonly RetentionConfig _config;
    private readonly Dictionary<string, Queue<TickEntry>> _ticks = new();
    private long _totalEvicted; // cumulative evictions across all symbols
    private DateTime? _lastEvictionUtc;
    private int _lastEvictedCount;
    private readonly object _lock = new();

    public TickRetentionManager(RetentionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public RetentionResult OnNewTick(string symbol, long sequence, DateTime timestampUtc)
    {
        lock (_lock)
        {
            if (!_ticks.TryGetValue(symbol, out var queue))
            {
                queue = new Queue<TickEntry>();
                _ticks[symbol] = queue;
            }

            queue.Enqueue(new TickEntry(sequence, timestampUtc));

            var evicted = 0;
            // Evict while either retention rule violated
            while (queue.Count > 0 && (queue.Count > _config.MaxTicksPerSymbol || (timestampUtc - queue.Peek().TimestampUtc) > _config.RetentionTime))
            {
                queue.Dequeue();
                evicted++;
            }

            if (evicted > 0)
            {
                _totalEvicted += evicted;
                _lastEvictionUtc = timestampUtc;
                _lastEvictedCount = evicted;
            }

            return new RetentionResult(evicted, queue.Count, _totalEvicted, _lastEvictionUtc, _lastEvictedCount);
        }
    }

    public RetentionMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new RetentionMetrics(_totalEvicted, _lastEvictionUtc, _lastEvictedCount);
        }
    }
}

public interface ITickRetentionManager
{
    RetentionResult OnNewTick(string symbol, long sequence, DateTime timestampUtc);
    RetentionMetrics GetMetrics();
}

internal readonly record struct TickEntry(long Sequence, DateTime TimestampUtc);

public readonly record struct RetentionResult(
    int EvictedCount,
    int CurrentCount,
    long TotalEvicted,
    DateTime? LastEvictionUtc,
    int LastEvictedBatchCount
);

public sealed record RetentionMetrics(
    long TotalEvicted,
    DateTime? LastEvictionUtc,
    int LastEvictedBatchCount
);