using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using System.Collections.Concurrent;

namespace MarketDataExcelUpdater.Infrastructure;

/// <summary>
/// In-memory implementation of IExcelWriter for testing scenarios.
/// Records queued updates and simulates column management without actual Excel operations.
/// </summary>
public sealed class InMemoryExcelWriter : IExcelWriter
{
    private readonly ConcurrentQueue<UpdateBatch> _queuedBatches = new();
    private readonly HashSet<string> _ensuredColumns = new();
    private readonly object _columnsLock = new();

    public List<UpdateBatch> ProcessedBatches { get; } = new();
    public IReadOnlySet<string> EnsuredColumns 
    {
        get
        {
            lock (_columnsLock)
            {
                return _ensuredColumns.ToHashSet();
            }
        }
    }

    public int TotalUpdatesProcessed { get; private set; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken ct = default)
    {
        // Process all queued batches
        var processedCount = 0;
        while (_queuedBatches.TryDequeue(out var batch))
        {
            ProcessedBatches.Add(batch);
            TotalUpdatesProcessed += batch.Updates.Count;
            processedCount++;
        }

        // Simulate some processing delay for realistic testing
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(UpdateBatch batch, CancellationToken ct = default)
    {
        // Non-blocking O(1) operation - just queue the batch
        _queuedBatches.Enqueue(batch);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Simulate ensuring required columns exist (for testing column auto-creation)
    /// </summary>
    public void EnsureColumns(IEnumerable<string> requiredHeaders)
    {
        lock (_columnsLock)
        {
            foreach (var header in requiredHeaders)
            {
                _ensuredColumns.Add(header);
            }
        }
    }

    /// <summary>
    /// Get the count of currently queued batches (for testing)
    /// </summary>
    public int QueuedBatchCount => _queuedBatches.Count;

    /// <summary>
    /// Clear all state (for test cleanup)
    /// </summary>
    public void Reset()
    {
        while (_queuedBatches.TryDequeue(out _)) { }
        ProcessedBatches.Clear();
        lock (_columnsLock)
        {
            _ensuredColumns.Clear();
        }
        TotalUpdatesProcessed = 0;
    }
}