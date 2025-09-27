namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Determines when to flush accumulated quotes into an UpdateBatch.
/// </summary>
public interface IBatchPolicy
{
    /// <summary>Signal that a quote arrived; returns true if a flush should occur now.</summary>
    bool ShouldFlush(Quote quote, DateTimeOffset now);

    /// <summary>Reset any internal counters after a flush.</summary>
    void Reset();
}
