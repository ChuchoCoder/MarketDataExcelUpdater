using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;
using MarketDataExcelUpdater.Core.Retention;
using MarketDataExcelUpdater.Core.Configuration;

namespace MarketDataExcelUpdater.Tests.Pipeline;

public class TickDispatcherTests
{
    private readonly Mock<ILogger<TickDispatcher>> _loggerMock;
    private readonly Mock<IStaleMonitor> _staleMonitorMock;
    private readonly TickDispatcher _tickDispatcher;
    private readonly ITickRetentionManager _retentionManager;
    private readonly DateTime _testDateTime = new(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

    public TickDispatcherTests()
    {
        _loggerMock = new Mock<ILogger<TickDispatcher>>();
        _staleMonitorMock = new Mock<IStaleMonitor>();
        _retentionManager = new TickRetentionManager(new RetentionConfig
        {
            MaxTicksPerSymbol = 2,
            RetentionTime = TimeSpan.FromMinutes(10)
        });
        _tickDispatcher = new TickDispatcher(_staleMonitorMock.Object, _loggerMock.Object, _retentionManager);
    }

    [Fact]
    public void ProcessQuote_WithNewSymbol_CreatesInstrumentAndQueuesUpdate()
    {
        // Arrange
        var quote = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );
        const string symbol = "TEST";
        const long sequence = 1;

        // Act
        _tickDispatcher.ProcessQuote(quote, symbol, sequence);

        // Assert
        var batch = _tickDispatcher.ExtractCurrentBatch();
        batch.Updates.Should().NotBeEmpty();
        batch.Updates.Should().Contain(u => u.SheetName == "MarketData" && u.ColumnName == "Symbol" && u.Value!.ToString() == symbol);
        batch.Updates.Should().Contain(u => u.SheetName == "MarketData" && u.ColumnName == "Last" && (decimal)u.Value! == 100.50m);
        
        // Verify stale monitor was called
        _staleMonitorMock.Verify(x => x.Observe(symbol, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public void ProcessQuote_WithExistingSymbol_UpdatesInstrument()
    {
        // Arrange
        const string symbol = "TEST";
        var quote1 = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );
        var quote2 = new Quote(
            Bid: 100.50m,
            BidSize: 1200,
            Ask: 101.00m,
            AskSize: 1300,
            Last: 101.00m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime.AddSeconds(1)
        );

        // Act - Process first quote
        _tickDispatcher.ProcessQuote(quote1, symbol, 1);
        var batch1 = _tickDispatcher.ExtractCurrentBatch();

        // Act - Process second quote
        _tickDispatcher.ProcessQuote(quote2, symbol, 2);
        var batch2 = _tickDispatcher.ExtractCurrentBatch();

        // Assert
        batch2.Updates.Should().Contain(u => u.SheetName == "MarketData" && u.ColumnName == "Last" && (decimal)u.Value! == 101.00m);
        
        // Verify stale monitor was called for both quotes
        _staleMonitorMock.Verify(x => x.Observe(symbol, It.IsAny<DateTimeOffset>()), Times.Exactly(2));
    }

    [Fact]
    public void ProcessQuote_WithOlderTimestamp_RejectsUpdate()
    {
        // Arrange
        const string symbol = "TEST";
        var newerQuote = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime.AddSeconds(10)
        );
        var olderQuote = new Quote(
            Bid: 99.25m,
            BidSize: 900,
            Ask: 99.75m,
            AskSize: 1100,
            Last: 99.00m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );

        // Act - Process newer quote first
        _tickDispatcher.ProcessQuote(newerQuote, symbol, 2);
        _tickDispatcher.ExtractCurrentBatch(); // Clear batch

        // Act - Process older quote
        _tickDispatcher.ProcessQuote(olderQuote, symbol, 1);
        var batch = _tickDispatcher.ExtractCurrentBatch();

        // Assert - Should be empty since older quote was rejected
        batch.Updates.Should().BeEmpty();
        
        // Verify stale monitor was only called for the first (accepted) quote
        _staleMonitorMock.Verify(x => x.Observe(symbol, It.IsAny<DateTimeOffset>()), Times.Once);
    }

    [Fact]
    public void ExtractCurrentBatch_ReturnsUpdatesAndClearsBatch()
    {
        // Arrange
        var quote = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );
        _tickDispatcher.ProcessQuote(quote, "TEST", 1);

        // Act
        var batch1 = _tickDispatcher.ExtractCurrentBatch();
        var batch2 = _tickDispatcher.ExtractCurrentBatch();

        // Assert
        batch1.Updates.Should().NotBeEmpty();
        batch2.Updates.Should().BeEmpty(); // Should be empty after extraction
    }

    [Fact]
    public void Retention_manager_evicts_excess_ticks()
    {
        var baseTime = _testDateTime;
        var q1 = new Quote(null,null,null,null,100m,null,null,null,null,null,null,null,null, baseTime);
        var q2 = new Quote(null,null,null,null,101m,null,null,null,null,null,null,null,null, baseTime.AddSeconds(1));
        var q3 = new Quote(null,null,null,null,102m,null,null,null,null,null,null,null,null, baseTime.AddSeconds(2));

        _tickDispatcher.ProcessQuote(q1, "RET", 1);
        _tickDispatcher.ProcessQuote(q2, "RET", 2);
        _tickDispatcher.ProcessQuote(q3, "RET", 3); // triggers eviction (max 2)

        var batch = _tickDispatcher.ExtractCurrentBatch();
        batch.Updates.Should().NotBeEmpty(); // still producing updates
        // We cannot directly assert internal retention queue, but metrics should reflect total evicted = 1
        var retentionMetrics = (_retentionManager as TickRetentionManager)!.GetMetrics();
        retentionMetrics.TotalEvicted.Should().Be(1);
    }

    [Fact]
    public void UpdateStaleFlags_LogsStaleCount()
    {
        // Arrange - Add some instruments first
        var quote = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );
        _tickDispatcher.ProcessQuote(quote, "TEST1", 1);
        _tickDispatcher.ProcessQuote(quote, "TEST2", 1);

        // Act
        _tickDispatcher.UpdateStaleFlags(_testDateTime);

        // Assert - Just verify it doesn't throw (logging is tested via mock verification if needed)
        // In a real implementation, this would interact with the stale monitor to detect and queue stale flag updates
    }

    [Fact]
    public void GetInstruments_ReturnsTrackedInstruments()
    {
        // Arrange
        var quote = new Quote(
            Bid: 100.25m,
            BidSize: 1000,
            Ask: 100.75m,
            AskSize: 1500,
            Last: 100.50m,
            Change: null,
            Open: null,
            High: null,
            Low: null,
            PreviousClose: null,
            Turnover: null,
            Volume: null,
            Operations: null,
            EventTimeArt: _testDateTime
        );
        _tickDispatcher.ProcessQuote(quote, "TEST", 1);

        // Act
        var instruments = _tickDispatcher.GetInstruments();

        // Assert
        instruments.Should().ContainKey("TEST");
        instruments["TEST"].Symbol.Should().Be("TEST");
    }
}