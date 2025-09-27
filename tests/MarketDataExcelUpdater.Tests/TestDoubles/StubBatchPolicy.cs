using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Tests.TestDoubles;

public sealed class StubBatchPolicy : IBatchPolicy
{
    private readonly int _countThreshold;
    private int _count;

    public StubBatchPolicy(int countThreshold) => _countThreshold = countThreshold;

    public bool ShouldFlush(Quote quote, DateTimeOffset now)
    {
        _count++;
        return _count >= _countThreshold;
    }

    public void Reset() => _count = 0;
}
