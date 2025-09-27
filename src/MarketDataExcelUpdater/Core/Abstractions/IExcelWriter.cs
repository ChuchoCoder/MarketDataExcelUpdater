namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Writes batches of cell updates to an Excel workbook, handling sheet auto-creation and column binding.
/// </summary>
public interface IExcelWriter : IAsyncDisposable
{
    /// <summary>Apply an update batch to the workbook (upsert semantics per bound row).</summary>
    ValueTask WriteAsync(UpdateBatch batch, CancellationToken ct = default);

    /// <summary>Flush any internal buffers and persist to disk.</summary>
    ValueTask FlushAsync(CancellationToken ct = default);
}
