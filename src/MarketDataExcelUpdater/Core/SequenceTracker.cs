using MarketDataExcelUpdater.Core.Abstractions;
using System.Collections.Concurrent;

namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Tracks per-symbol sequence numbers and detects gaps/duplicates.
/// Thread-safe implementation using concurrent collections.
/// </summary>
public sealed class SequenceTracker : ISequenceTracker
{
    private readonly ConcurrentDictionary<string, long> _lastSequence = new();

    public SequenceClassification Register(string symbol, long sequence)
    {
        var lastSequence = _lastSequence.GetValueOrDefault(symbol, -1);

        // Update the stored sequence number
        _lastSequence.AddOrUpdate(symbol, sequence, (_, _) => sequence);

        // Classify based on comparison with previous sequence
        if (lastSequence == -1)
        {
            return SequenceClassification.InOrder; // First sequence for this symbol
        }
        else if (sequence == lastSequence)
        {
            return SequenceClassification.Duplicate;
        }
        else if (sequence == lastSequence + 1)
        {
            return SequenceClassification.InOrder;
        }
        else
        {
            return SequenceClassification.Gap; // Either jumped ahead or went backwards
        }
    }
}