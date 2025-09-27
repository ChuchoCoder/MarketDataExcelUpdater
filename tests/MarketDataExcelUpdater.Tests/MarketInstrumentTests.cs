using FluentAssertions;
using MarketDataExcelUpdater.Core;

namespace MarketDataExcelUpdater.Tests;

public sealed class MarketInstrumentTests
{
    private static Quote CreateTestQuote(DateTime timestamp, decimal? last = 100.0m) =>
        new(null, null, null, null, last, null, null, null, null, null, null, null, null, timestamp);

    [Fact]
    public void Constructor_requires_non_empty_symbol()
    {
        var act = () => new MarketInstrument("");
        act.Should().Throw<ArgumentException>().WithMessage("*Symbol cannot be null or empty*");
    }

    [Fact]
    public void TryUpdate_accepts_first_quote()
    {
        var instrument = new MarketInstrument("GGAL");
        var quote = CreateTestQuote(DateTime.Now);

        var result = instrument.TryUpdate(quote);

        result.Should().BeTrue();
        instrument.LastQuote.Should().Be(quote);
        instrument.LastUpdateTime.Should().Be(quote.EventTimeArt);
    }

    [Fact]
    public void TryUpdate_enforces_monotonic_timestamps()
    {
        var instrument = new MarketInstrument("YPFD");
        var time1 = DateTime.Now;
        var time2 = time1.AddSeconds(-10); // Earlier time

        instrument.TryUpdate(CreateTestQuote(time1));
        var result = instrument.TryUpdate(CreateTestQuote(time2, 200.0m));

        result.Should().BeFalse();
        instrument.LastQuote!.Last.Should().Be(100.0m); // Should still be first quote
        instrument.LastUpdateTime.Should().Be(time1);
    }

    [Fact]
    public void TryUpdate_allows_equal_timestamps()
    {
        var instrument = new MarketInstrument("PAMP");
        var time = DateTime.Now;

        instrument.TryUpdate(CreateTestQuote(time, 50.0m));
        var result = instrument.TryUpdate(CreateTestQuote(time, 55.0m));

        result.Should().BeTrue();
        instrument.LastQuote!.Last.Should().Be(55.0m);
    }

    [Fact]
    public void TryUpdate_filters_negative_bid_values()
    {
        var instrument = new MarketInstrument("ALUA");
        var quote = new Quote(-10.0m, null, null, null, null, null, null, null, null, null, null, null, null, DateTime.Now);

        instrument.TryUpdate(quote);

        instrument.LastQuote!.Bid.Should().BeNull();
    }

    [Fact]
    public void TryUpdate_filters_negative_ask_values()
    {
        var instrument = new MarketInstrument("TXAR");
        var quote = new Quote(null, null, -5.0m, null, null, null, null, null, null, null, null, null, null, DateTime.Now);

        instrument.TryUpdate(quote);

        instrument.LastQuote!.Ask.Should().BeNull();
    }

    [Fact]
    public void TryUpdate_preserves_negative_change_values()
    {
        var instrument = new MarketInstrument("COME");
        var quote = new Quote(null, null, null, null, null, -2.5m, null, null, null, null, null, null, null, DateTime.Now);

        instrument.TryUpdate(quote);

        instrument.LastQuote!.Change.Should().Be(-2.5m); // Change can be negative
    }

    [Fact]
    public void TryUpdate_filters_negative_volume_values()
    {
        var instrument = new MarketInstrument("CRES");
        var quote = new Quote(null, null, null, null, null, null, null, null, null, null, null, -100L, null, DateTime.Now);

        instrument.TryUpdate(quote);

        instrument.LastQuote!.Volume.Should().BeNull();
    }

    [Fact]
    public void Stale_property_can_be_set_by_external_monitor()
    {
        var instrument = new MarketInstrument("TEST");
        
        instrument.Stale.Should().BeFalse(); // Default
        
        instrument.Stale = true;
        instrument.Stale.Should().BeTrue();
    }

    [Fact]
    public void VariantType_defaults_to_spot()
    {
        var instrument = new MarketInstrument("GGAL");
        instrument.VariantType.Should().Be(VariantType.Spot);
    }

    [Fact]
    public void VariantType_can_be_set_explicitly()
    {
        var instrument = new MarketInstrument("GGAL-24hs", VariantType.Settlement24h);
        instrument.VariantType.Should().Be(VariantType.Settlement24h);
    }

    // New sequence-related tests for Task A11
    [Fact]
    public void TryUpdate_with_sequence_first_time_should_be_InOrder()
    {
        var instrument = new MarketInstrument("GGAL");
        var quote = CreateTestQuote(DateTime.Now);

        var result = instrument.TryUpdate(quote, 1);

        result.Success.Should().BeTrue();
        result.SequenceResult.Should().Be(SequenceResult.InOrder);
        instrument.LastSequence.Should().Be(1);
    }

    [Fact]
    public void TryUpdate_with_sequential_sequences_should_be_InOrder()
    {
        var instrument = new MarketInstrument("YPFD");
        var time = DateTime.Now;
        
        instrument.TryUpdate(CreateTestQuote(time), 10);
        var result = instrument.TryUpdate(CreateTestQuote(time.AddSeconds(1)), 11);

        result.Success.Should().BeTrue();
        result.SequenceResult.Should().Be(SequenceResult.InOrder);
        instrument.LastSequence.Should().Be(11);
    }

    [Fact]
    public void TryUpdate_with_gap_should_detect_gap_and_increment_counter()
    {
        var instrument = new MarketInstrument("PAMP");
        var time = DateTime.Now;
        
        instrument.TryUpdate(CreateTestQuote(time), 5);
        var result = instrument.TryUpdate(CreateTestQuote(time.AddSeconds(1)), 10); // Gap from 5 to 10

        result.Success.Should().BeTrue();
        result.SequenceResult.Should().Be(SequenceResult.Gap);
        result.GapsDetected.Should().Be(1);
        instrument.GapCount.Should().Be(1);
        instrument.LastSequence.Should().Be(10);
    }

    [Fact]
    public void TryUpdate_with_duplicate_sequence_should_detect_duplicate()
    {
        var instrument = new MarketInstrument("ALUA");
        var time = DateTime.Now;
        
        instrument.TryUpdate(CreateTestQuote(time), 3);
        var result = instrument.TryUpdate(CreateTestQuote(time.AddSeconds(1)), 3); // Same sequence

        result.Success.Should().BeTrue();
        result.SequenceResult.Should().Be(SequenceResult.Duplicate);
        instrument.LastSequence.Should().Be(3); // Updated to duplicate sequence
    }

    [Fact]
    public void TryUpdate_with_backwards_sequence_should_detect_gap()
    {
        var instrument = new MarketInstrument("TXAR");
        var time = DateTime.Now;
        
        instrument.TryUpdate(CreateTestQuote(time), 100);
        var result = instrument.TryUpdate(CreateTestQuote(time.AddSeconds(1)), 95); // Backwards

        result.Success.Should().BeTrue();
        result.SequenceResult.Should().Be(SequenceResult.Gap);
        result.GapsDetected.Should().Be(1);
    }

    [Fact]
    public void TryUpdate_legacy_method_still_works_without_sequence()
    {
        var instrument = new MarketInstrument("COME");
        var quote = CreateTestQuote(DateTime.Now);

        var result = instrument.TryUpdate(quote);

        result.Should().BeTrue();
        instrument.LastSequence.Should().Be(-1); // No sequence set
    }

    [Fact]
    public void Multiple_gaps_accumulate_in_counter()
    {
        var instrument = new MarketInstrument("CRES");
        var time = DateTime.Now;
        
        instrument.TryUpdate(CreateTestQuote(time), 1);
        instrument.TryUpdate(CreateTestQuote(time.AddSeconds(1)), 5); // Gap 1
        var result = instrument.TryUpdate(CreateTestQuote(time.AddSeconds(2)), 10); // Gap 2

        result.GapsDetected.Should().Be(2);
        instrument.GapCount.Should().Be(2);
    }
}