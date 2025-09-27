namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Writes a durable append-only log of raw quotes for replay / debugging.
/// </summary>
public interface IReplayWriter : IAsyncDisposable
{
    ValueTask AppendAsync(Quote quote, CancellationToken ct = default);
    ValueTask FlushAsync(CancellationToken ct = default);
}
