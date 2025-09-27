namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Volatile aggregate metrics snapshot.
/// </summary>
public sealed class MetricsSnapshot
{
    public long TicksReceived { get; set; }
    public long TicksWritten { get; set; }
    public long DroppedTicks { get; set; }
    public int StaleCount { get; set; }
    public int ReconnectCount { get; set; }
    public double LastFlushLatencyMs { get; set; }
    public double? LatencyP95Ms { get; set; }
}
