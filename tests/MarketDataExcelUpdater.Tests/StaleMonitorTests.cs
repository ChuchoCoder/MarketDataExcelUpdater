using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Tests;

public sealed class StaleMonitorTests
{
    [Fact]
    public void Symbol_becomes_stale_after_threshold()
    {
        IStaleMonitor monitor = new StaleMonitor();
        var start = DateTimeOffset.UtcNow;
        monitor.Observe("GGAL", start);

        var stale = monitor.ConsumeNewlyStale(TimeSpan.FromSeconds(5), start.AddSeconds(6));
        stale.Should().ContainSingle().Which.Should().Be("GGAL");
    }

    [Fact]
    public void Stale_symbol_recovers_when_new_quote_arrives()
    {
        IStaleMonitor monitor = new StaleMonitor();
        var start = DateTimeOffset.UtcNow;
        monitor.Observe("YPFD", start);
        // Become stale
        _ = monitor.ConsumeNewlyStale(TimeSpan.FromSeconds(5), start.AddSeconds(6));

        // New quote triggers recovery
        monitor.Observe("YPFD", start.AddSeconds(7));
        var recovered = monitor.ConsumeRecovered(start.AddSeconds(7), TimeSpan.FromSeconds(5));
        recovered.Should().ContainSingle().Which.Should().Be("YPFD");
    }
}
