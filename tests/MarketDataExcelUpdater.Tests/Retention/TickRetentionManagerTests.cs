using FluentAssertions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Core.Retention;

namespace MarketDataExcelUpdater.Tests.Retention;

public class TickRetentionManagerTests
{
    private static TickRetentionManager Create(int maxCount = 3, int retentionMinutes = 10)
    {
        var cfg = new RetentionConfig
        {
            MaxTicksPerSymbol = maxCount,
            RetentionTime = TimeSpan.FromMinutes(retentionMinutes)
        };
        return new TickRetentionManager(cfg);
    }

    [Fact]
    public void Evicts_when_count_exceeds_max()
    {
        var mgr = Create(maxCount: 2);
        var now = DateTime.UtcNow;
        mgr.OnNewTick("SYM", 1, now);
        mgr.OnNewTick("SYM", 2, now.AddSeconds(1));
        var result = mgr.OnNewTick("SYM", 3, now.AddSeconds(2));
        result.EvictedCount.Should().Be(1);
        result.CurrentCount.Should().Be(2);
        result.TotalEvicted.Should().Be(1);
    }

    [Fact]
    public void Evicts_when_oldest_exceeds_age()
    {
        var mgr = Create(maxCount: 10, retentionMinutes: 1);
        var start = DateTime.UtcNow.AddMinutes(-2);
        mgr.OnNewTick("SYM", 1, start);
        var result = mgr.OnNewTick("SYM", 2, DateTime.UtcNow);
        result.EvictedCount.Should().Be(1);
        result.TotalEvicted.Should().Be(1);
    }

    [Fact]
    public void No_eviction_when_within_limits()
    {
        var mgr = Create(maxCount: 5, retentionMinutes: 5);
        var now = DateTime.UtcNow;
        mgr.OnNewTick("SYM", 1, now);
        var result = mgr.OnNewTick("SYM", 2, now.AddSeconds(10));
        result.EvictedCount.Should().Be(0);
        result.CurrentCount.Should().Be(2);
    }

    [Fact]
    public void Metrics_reflect_cumulative_evictions()
    {
        var mgr = Create(maxCount: 2);
        var now = DateTime.UtcNow;
        mgr.OnNewTick("SYM", 1, now);
        mgr.OnNewTick("SYM", 2, now.AddSeconds(1));
        mgr.OnNewTick("SYM", 3, now.AddSeconds(2)); // evict 1
        mgr.OnNewTick("SYM", 4, now.AddSeconds(3)); // evict 1
        var metrics = mgr.GetMetrics();
        metrics.TotalEvicted.Should().Be(2);
        metrics.LastEvictedBatchCount.Should().Be(1);
        metrics.LastEvictionUtc.Should().NotBeNull();
    }
}
