namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Tracks per-symbol sequence numbers and detects gaps / duplicates.
/// </summary>
public interface ISequenceTracker
{
    /// <summary>Register an observed sequence for a symbol. Returns classification (Gap, Duplicate, InOrder).</summary>
    SequenceClassification Register(string symbol, long sequence);
}

public enum SequenceClassification
{
    InOrder,
    Gap,
    Duplicate
}
