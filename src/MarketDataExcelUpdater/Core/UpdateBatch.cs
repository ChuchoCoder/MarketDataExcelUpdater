namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Aggregates pending cell updates for a single flush cycle.
/// </summary>
public sealed class UpdateBatch
{
    public List<CellUpdate> Updates { get; } = new();
    public DateTime? OldestEnqueuedUtc { get; private set; }

    public UpdateBatch()
    {
    }

    public UpdateBatch(IEnumerable<CellUpdate> updates)
    {
        Updates.AddRange(updates);
        if (Updates.Count > 0)
        {
            OldestEnqueuedUtc = DateTime.UtcNow;
        }
    }

    public void Enqueue(CellUpdate update, DateTime nowUtc)
    {
        Updates.Add(update);
        OldestEnqueuedUtc ??= nowUtc;
    }

    public void Clear()
    {
        Updates.Clear();
        OldestEnqueuedUtc = null;
    }
}
