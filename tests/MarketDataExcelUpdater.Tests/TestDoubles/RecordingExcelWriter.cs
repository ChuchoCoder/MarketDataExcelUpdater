using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using System.Collections.Concurrent;

namespace MarketDataExcelUpdater.Tests.TestDoubles;

public sealed class RecordingExcelWriter : IExcelWriter
{
    public ConcurrentQueue<UpdateBatch> Batches { get; } = new();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask FlushAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask WriteAsync(UpdateBatch batch, CancellationToken ct = default)
    {
        Batches.Enqueue(batch);
        return ValueTask.CompletedTask;
    }
}
