using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;

namespace MarketDataExcelUpdater.Tests.Pipeline;

public class FlushOrchestratorTests : IAsyncDisposable
{
    private readonly Mock<ILogger<FlushOrchestrator>> _loggerMock;
    private readonly Mock<IExcelWriter> _excelWriterMock;
    private readonly Mock<IBatchPolicy> _batchPolicyMock;
    private readonly Mock<IStaleMonitor> _staleMonitorMock;
    private readonly TickDispatcher _tickDispatcher;
    private readonly FlushOrchestrator _flushOrchestrator;

    public FlushOrchestratorTests()
    {
        _loggerMock = new Mock<ILogger<FlushOrchestrator>>();
        _excelWriterMock = new Mock<IExcelWriter>();
        _batchPolicyMock = new Mock<IBatchPolicy>();
        _staleMonitorMock = new Mock<IStaleMonitor>();
        
        // Create a real TickDispatcher for testing
        var tickDispatcherLogger = new Mock<ILogger<TickDispatcher>>();
        _tickDispatcher = new TickDispatcher(_staleMonitorMock.Object, tickDispatcherLogger.Object);
        
        _flushOrchestrator = new FlushOrchestrator(
            _tickDispatcher,
            _excelWriterMock.Object,
            _batchPolicyMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void ProcessQuote_ThroughTickDispatcher_TracksInstruments()
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
            EventTimeArt: DateTime.UtcNow
        );
        const string symbol = "TEST";
        const long sequence = 1;

        // Act - Process through tick dispatcher (which FlushOrchestrator uses)
        _tickDispatcher.ProcessQuote(quote, symbol, sequence);

        // Assert
        var instruments = _tickDispatcher.GetInstruments();
        instruments.Should().ContainKey(symbol);
        instruments[symbol].LastQuote.Should().NotBeNull();
        instruments[symbol].LastQuote!.Last.Should().Be(100.50m);
    }

    [Fact]
    public async Task FlushNowAsync_FlushesImmediatelyAndResetsPolicy()
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
            EventTimeArt: DateTime.UtcNow
        );
        
        // Setup mocks
        _excelWriterMock.Setup(x => x.WriteAsync(It.IsAny<UpdateBatch>(), It.IsAny<CancellationToken>()))
                       .Returns(ValueTask.CompletedTask);
        _excelWriterMock.Setup(x => x.FlushAsync(It.IsAny<CancellationToken>()))
                       .Returns(ValueTask.CompletedTask);

        // Add a quote to create some updates
        _tickDispatcher.ProcessQuote(quote, "TEST", 1);

        // Act
        await _flushOrchestrator.FlushNowAsync();

        // Assert
        _excelWriterMock.Verify(x => x.WriteAsync(It.IsAny<UpdateBatch>(), It.IsAny<CancellationToken>()), Times.Once);
        _excelWriterMock.Verify(x => x.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
        _batchPolicyMock.Verify(x => x.Reset(), Times.Once);
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
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
            EventTimeArt: DateTime.UtcNow
        );

        // Act - Process quotes through tick dispatcher
        _tickDispatcher.ProcessQuote(quote, "TEST1", 1);
        _tickDispatcher.ProcessQuote(quote, "TEST2", 2);
        
        var stats = _flushOrchestrator.GetStats();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalFlushes.Should().BeGreaterOrEqualTo(0);
        stats.PendingUpdates.Should().BeGreaterThan(0); // Should have updates from the processed quotes
    }

    [Fact] 
    public void FlushOrchestrator_StartsWithZeroStats()
    {
        // Act
        var stats = _flushOrchestrator.GetStats();

        // Assert
        stats.TotalQuotesProcessed.Should().Be(0);
        stats.TotalFlushes.Should().Be(0);
        stats.PendingUpdates.Should().Be(0);
    }

    [Fact]
    public async Task DisposeAsync_CompletesSuccessfully()
    {
        // Act & Assert - should not throw
        await _flushOrchestrator.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _flushOrchestrator.DisposeAsync();
    }
}