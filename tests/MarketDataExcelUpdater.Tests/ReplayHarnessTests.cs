using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Infrastructure;
using System.Text.Json;

namespace MarketDataExcelUpdater.Tests;

/// <summary>
/// Tests for ReplayHarness - these should initially pass since ReplayHarness is implemented,
/// but test end-to-end replay scenarios for FR-028
/// </summary>
public sealed class ReplayHarnessTests
{
    private const string SampleTickJson = """
        [
            {
                "symbol": "GGAL",
                "timestamp": "2025-09-25T10:00:00",
                "sequence": 1,
                "bid": 150.0,
                "ask": 150.5,
                "last": 150.25
            },
            {
                "symbol": "GGAL",
                "timestamp": "2025-09-25T10:00:01",
                "sequence": 2,
                "bid": 150.1,
                "ask": 150.6,
                "last": 150.30
            },
            {
                "symbol": "YPFD",
                "timestamp": "2025-09-25T10:00:02", 
                "sequence": 1,
                "bid": 45.0,
                "ask": 45.2,
                "last": 45.1
            }
        ]
        """;

    [Fact]
    public void LoadFromJson_should_parse_sample_ticks()
    {
        var harness = new ReplayHarness();
        
        harness.LoadFromJson(SampleTickJson);
        
        var ticks = harness.ReplayAll().ToArray();
        ticks.Should().HaveCount(3);
        ticks[0].Quote.Last.Should().Be(150.25m);
        ticks[2].Quote.Last.Should().Be(45.1m);
    }

    [Fact]
    public void ReplayAll_should_return_ticks_in_chronological_order()
    {
        var harness = new ReplayHarness();
        harness.LoadFromJson(SampleTickJson);
        
        var ticks = harness.ReplayAll().ToArray();
        
        // Should be sorted by timestamp
        ticks[0].Quote.EventTimeArt.Should().Be(DateTime.Parse("2025-09-25T10:00:00"));
        ticks[1].Quote.EventTimeArt.Should().Be(DateTime.Parse("2025-09-25T10:00:01"));
        ticks[2].Quote.EventTimeArt.Should().Be(DateTime.Parse("2025-09-25T10:00:02"));
    }

    [Fact]
    public void ReplayAll_should_include_sequence_numbers()
    {
        var harness = new ReplayHarness();
        harness.LoadFromJson(SampleTickJson);
        
        var ticks = harness.ReplayAll().ToArray();
        
        ticks[0].Sequence.Should().Be(1);
        ticks[1].Sequence.Should().Be(2);
        ticks[2].Sequence.Should().Be(1); // YPFD sequence starts at 1
    }

    [Fact]
    public void End_to_end_replay_should_update_instruments_correctly()
    {
        // This is the main integration test for replay functionality
        var harness = new ReplayHarness();
        harness.LoadFromJson(SampleTickJson);
        
        // Create instruments to replay into
        var ggalInstrument = new MarketInstrument("GGAL");
        var ypfdInstrument = new MarketInstrument("YPFD");
        var instruments = new Dictionary<string, MarketInstrument>
        {
            ["GGAL"] = ggalInstrument,
            ["YPFD"] = ypfdInstrument
        };
        
        // Replay all ticks
        foreach (var (quote, symbol, sequence) in harness.ReplayAll())
        {
            if (instruments.TryGetValue(symbol, out var instrument))
            {
                instrument.TryUpdate(quote, sequence);
            }
        }
        
        // Verify final state
        ggalInstrument.LastQuote!.Last.Should().Be(150.30m);
        ggalInstrument.LastSequence.Should().Be(2);
        ggalInstrument.GapCount.Should().Be(0); // No gaps in sequence
        
        ypfdInstrument.LastQuote!.Last.Should().Be(45.1m);
        ypfdInstrument.LastSequence.Should().Be(1);
        ypfdInstrument.GapCount.Should().Be(0);
    }

    [Fact]
    public void GetFinalState_should_provide_verification_summary()
    {
        var harness = new ReplayHarness();
        harness.LoadFromJson(SampleTickJson);
        
        var instruments = new[]
        {
            new MarketInstrument("GGAL"),
            new MarketInstrument("YPFD")
        };
        
        // Simulate some updates
        instruments[0].TryUpdate(CreateTestQuote(DateTime.Now), 5);
        instruments[1].TryUpdate(CreateTestQuote(DateTime.Now), 10);
        
        var state = harness.GetFinalState(instruments);
        
        state.TotalTicks.Should().Be(3);
        state.UniqueSymbols.Should().Be(2);
        state.InstrumentStates.Should().ContainKeys("GGAL", "YPFD");
        state.InstrumentStates["GGAL"].LastSequence.Should().Be(5);
        state.InstrumentStates["YPFD"].LastSequence.Should().Be(10);
    }

    [Fact]
    public void Replay_with_gaps_should_be_detected()
    {
        // Test with gap in sequence
        var gapTickJson = """
            [
                {
                    "symbol": "TEST",
                    "timestamp": "2025-09-25T10:00:00",
                    "sequence": 1,
                    "last": 100.0
                },
                {
                    "symbol": "TEST", 
                    "timestamp": "2025-09-25T10:00:01",
                    "sequence": 5,
                    "last": 105.0
                }
            ]
            """;
        
        var harness = new ReplayHarness();
        harness.LoadFromJson(gapTickJson);
        
        var instrument = new MarketInstrument("TEST");
        
        foreach (var (quote, symbol, sequence) in harness.ReplayAll())
        {
            instrument.TryUpdate(quote, sequence);
        }
        
        instrument.GapCount.Should().Be(1); // Should detect gap from 1 to 5
        instrument.LastSequence.Should().Be(5);
    }

    private static Quote CreateTestQuote(DateTime timestamp) =>
        new(null, null, null, null, 100.0m, null, null, null, null, null, null, null, null, timestamp);
}