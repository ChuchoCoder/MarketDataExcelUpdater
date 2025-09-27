using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Tests;

public sealed class SequenceTrackerTests
{
    [Fact]
    public void First_sequence_for_symbol_is_InOrder()
    {
        var tracker = new SequenceTracker();
        
        var result = tracker.Register("GGAL", 1);
        
        result.Should().Be(SequenceClassification.InOrder);
    }

    [Fact]
    public void Consecutive_sequences_are_InOrder()
    {
        var tracker = new SequenceTracker();
        tracker.Register("YPFD", 100);
        
        var result = tracker.Register("YPFD", 101);
        
        result.Should().Be(SequenceClassification.InOrder);
    }

    [Fact]
    public void Duplicate_sequence_detected()
    {
        var tracker = new SequenceTracker();
        tracker.Register("PAMP", 50);
        
        var result = tracker.Register("PAMP", 50);
        
        result.Should().Be(SequenceClassification.Duplicate);
    }

    [Fact]
    public void Gap_detected_when_sequence_jumps()
    {
        var tracker = new SequenceTracker();
        tracker.Register("ALUA", 10);
        
        var result = tracker.Register("ALUA", 15);
        
        result.Should().Be(SequenceClassification.Gap);
    }

    [Fact]
    public void Backwards_sequence_detected_as_gap()
    {
        var tracker = new SequenceTracker();
        tracker.Register("TXAR", 100);
        
        var result = tracker.Register("TXAR", 95);
        
        result.Should().Be(SequenceClassification.Gap);
    }

    [Fact]
    public void Different_symbols_tracked_independently()
    {
        var tracker = new SequenceTracker();
        tracker.Register("GGAL", 10);
        tracker.Register("YPFD", 20);
        
        var ggalResult = tracker.Register("GGAL", 11);
        var ypfdResult = tracker.Register("YPFD", 22);
        
        ggalResult.Should().Be(SequenceClassification.InOrder);
        ypfdResult.Should().Be(SequenceClassification.Gap);
    }
}