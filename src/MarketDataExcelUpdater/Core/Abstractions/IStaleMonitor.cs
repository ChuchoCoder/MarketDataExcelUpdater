namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Tracks quote freshness per instrument and raises transitions for stale/fresh states.
/// </summary>
public interface IStaleMonitor
{
    /// <summary>Record an observed quote timestamp for a symbol.</summary>
    void Observe(string symbol, DateTimeOffset exchangeTimestamp);

    /// <summary>Return symbols that have transitioned to stale since last poll.</summary>
    IReadOnlyCollection<string> ConsumeNewlyStale(TimeSpan staleThreshold, DateTimeOffset now);

    /// <summary>Return symbols that have recovered (fresh again) since last poll.</summary>
    IReadOnlyCollection<string> ConsumeRecovered(DateTimeOffset now, TimeSpan staleThreshold);
}
