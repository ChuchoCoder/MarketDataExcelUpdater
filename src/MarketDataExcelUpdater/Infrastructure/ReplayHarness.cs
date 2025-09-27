using MarketDataExcelUpdater.Core;
using System.Text.Json;

namespace MarketDataExcelUpdater.Infrastructure;

/// <summary>
/// Replay harness for testing with historical tick data from JSON files.
/// Supports regression testing and debugging scenarios (FR-028).
/// </summary>
public sealed class ReplayHarness
{
    private readonly List<ReplayTick> _ticks = new();
    
    /// <summary>
    /// Load ticks from JSON file for replay scenarios
    /// </summary>
    public void LoadFromJson(string jsonContent)
    {
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        };
        
        var ticks = JsonSerializer.Deserialize<ReplayTick[]>(jsonContent, options);
        if (ticks != null)
        {
            _ticks.AddRange(ticks);
            _ticks.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        }
    }

    /// <summary>
    /// Replay all ticks in chronological order
    /// </summary>
    public IEnumerable<(Quote Quote, string Symbol, long Sequence)> ReplayAll()
    {
        foreach (var tick in _ticks)
        {
            yield return (ConvertToQuote(tick), tick.Symbol, tick.Sequence ?? 0);
        }
    }

    /// <summary>
    /// Get final state summary after replay (for verification)
    /// </summary>
    public ReplayState GetFinalState(IEnumerable<MarketInstrument> instruments)
    {
        return new ReplayState
        {
            TotalTicks = _ticks.Count,
            UniqueSymbols = _ticks.Select(t => t.Symbol).Distinct().Count(),
            InstrumentStates = instruments.ToDictionary(
                i => i.Symbol, 
                i => new InstrumentState
                {
                    LastPrice = i.LastQuote?.Last,
                    LastSequence = i.LastSequence,
                    GapCount = i.GapCount,
                    IsStale = i.Stale
                })
        };
    }

    private static Quote ConvertToQuote(ReplayTick tick)
    {
        return new Quote(
            Bid: tick.Bid,
            BidSize: tick.BidSize,
            Ask: tick.Ask,
            AskSize: tick.AskSize,
            Last: tick.Last,
            Change: tick.Change,
            Open: tick.Open,
            High: tick.High,
            Low: tick.Low,
            PreviousClose: tick.PreviousClose,
            Turnover: tick.Turnover,
            Volume: tick.Volume,
            Operations: tick.Operations,
            EventTimeArt: tick.Timestamp
        );
    }
}

/// <summary>
/// JSON-serializable tick data for replay scenarios
/// </summary>
public sealed class ReplayTick
{
    public string Symbol { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public long? Sequence { get; set; }
    public decimal? Bid { get; set; }
    public decimal? BidSize { get; set; }
    public decimal? Ask { get; set; }
    public decimal? AskSize { get; set; }
    public decimal? Last { get; set; }
    public decimal? Change { get; set; }
    public decimal? Open { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? Turnover { get; set; }
    public long? Volume { get; set; }
    public long? Operations { get; set; }
}

/// <summary>
/// Final state after replay for verification
/// </summary>
public sealed class ReplayState
{
    public int TotalTicks { get; init; }
    public int UniqueSymbols { get; init; }
    public Dictionary<string, InstrumentState> InstrumentStates { get; init; } = new();
}

public sealed class InstrumentState
{
    public decimal? LastPrice { get; init; }
    public long LastSequence { get; init; }
    public int GapCount { get; init; }
    public bool IsStale { get; init; }
}