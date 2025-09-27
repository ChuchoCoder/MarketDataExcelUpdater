namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Represents a subscribed market instrument with state management and quote updates.
/// </summary>
public sealed class MarketInstrument
{
    public string Symbol { get; }
    public VariantType VariantType { get; }
    public Quote? LastQuote { get; private set; }
    public DateTime LastUpdateTime { get; private set; }
    public bool Stale { get; set; } // Set by StaleMonitor
    public long LastSequence { get; private set; }
    public int GapCount { get; private set; }

    public MarketInstrument(string symbol, VariantType variantType = VariantType.Spot)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        Symbol = symbol;
        VariantType = variantType;
        LastUpdateTime = DateTime.MinValue;
        LastSequence = -1; // No sequence received yet
    }

    /// <summary>
    /// Update the instrument with a new quote, enforcing monotonic timestamp and negative value filtering.
    /// Returns result of sequence validation.
    /// </summary>
    public UpdateResult TryUpdate(Quote quote, long sequence)
    {
        // Enforce monotonic timestamp constraint (FR-003)
        if (quote.EventTimeArt < LastUpdateTime)
        {
            return UpdateResult.TimestampTooOld;
        }

        // Check sequence monotonic increment and detect gaps (FR-013, FR-030)
        var sequenceResult = ValidateSequence(sequence);
        if (sequenceResult == SequenceResult.Gap)
        {
            GapCount++;
        }

        // Filter negative values (FR-004) - create sanitized quote
        var sanitizedQuote = SanitizeQuote(quote);
        
        LastQuote = sanitizedQuote;
        LastUpdateTime = quote.EventTimeArt;
        LastSequence = sequence;
        
        return new UpdateResult 
        { 
            Success = true, 
            SequenceResult = sequenceResult,
            GapsDetected = GapCount 
        };
    }

    /// <summary>
    /// Legacy method for backward compatibility - calls new overload with sequence -1
    /// </summary>
    public bool TryUpdate(Quote quote)
    {
        var result = TryUpdate(quote, -1);
        return result.Success;
    }

    private SequenceResult ValidateSequence(long sequence)
    {
        if (sequence == -1) return SequenceResult.NoSequence; // Legacy calls
        
        if (LastSequence == -1) return SequenceResult.InOrder; // First sequence
        
        if (sequence == LastSequence) return SequenceResult.Duplicate;
        
        if (sequence == LastSequence + 1) return SequenceResult.InOrder;
        
        return SequenceResult.Gap; // Either jumped ahead or went backwards
    }

    private static Quote SanitizeQuote(Quote quote)
    {
        // Replace negative values with null (FR-004)
        return new Quote(
            Bid: quote.Bid >= 0 ? quote.Bid : null,
            BidSize: quote.BidSize >= 0 ? quote.BidSize : null,
            Ask: quote.Ask >= 0 ? quote.Ask : null,
            AskSize: quote.AskSize >= 0 ? quote.AskSize : null,
            Last: quote.Last >= 0 ? quote.Last : null,
            Change: quote.Change, // Change can be negative
            Open: quote.Open >= 0 ? quote.Open : null,
            High: quote.High >= 0 ? quote.High : null,
            Low: quote.Low >= 0 ? quote.Low : null,
            PreviousClose: quote.PreviousClose >= 0 ? quote.PreviousClose : null,
            Turnover: quote.Turnover >= 0 ? quote.Turnover : null,
            Volume: quote.Volume >= 0 ? quote.Volume : null,
            Operations: quote.Operations >= 0 ? quote.Operations : null,
            EventTimeArt: quote.EventTimeArt
        );
    }
}

public enum VariantType
{
    Spot,
    Settlement24h,
    Other
}

public enum SequenceResult
{
    NoSequence,
    InOrder,
    Gap,
    Duplicate
}

public sealed class UpdateResult
{
    public bool Success { get; init; }
    public SequenceResult SequenceResult { get; init; }
    public int GapsDetected { get; init; }
    
    public static UpdateResult TimestampTooOld => new() { Success = false };
}
