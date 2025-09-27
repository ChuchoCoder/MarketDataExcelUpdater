using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Batching policy that flushes based on count thresholds, time elapsed, or explicit triggers.
/// </summary>
public sealed class BatchPolicy : IBatchPolicy
{
    private readonly int _maxQuoteCount;
    private readonly TimeSpan _maxTimespan;
    private int _currentCount;
    private DateTimeOffset _firstQuoteTime;

    public BatchPolicy(int maxQuoteCount, TimeSpan maxTimespan)
    {
        _maxQuoteCount = maxQuoteCount;
        _maxTimespan = maxTimespan;
        Reset();
    }

    public bool ShouldFlush(Quote quote, DateTimeOffset now)
    {
        // If this is the first quote in the batch, record the start time
        if (_currentCount == 0)
        {
            _firstQuoteTime = now;
        }

        _currentCount++;

        // Flush if we hit the count threshold
        if (_currentCount >= _maxQuoteCount)
        {
            return true;
        }

        // Flush if enough time has elapsed since the first quote in this batch
        if (now - _firstQuoteTime >= _maxTimespan)
        {
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _currentCount = 0;
        _firstQuoteTime = DateTimeOffset.MinValue;
    }
}