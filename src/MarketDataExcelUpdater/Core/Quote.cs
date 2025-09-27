namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Immutable snapshot of market data values for a symbol at a point in time.
/// Behavior-free model used in tests & pipeline.
/// </summary>
public sealed record Quote(
    decimal? Bid,
    decimal? BidSize,
    decimal? Ask,
    decimal? AskSize,
    decimal? Last,
    decimal? Change,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? PreviousClose,
    decimal? Turnover,
    long? Volume,
    long? Operations,
    DateTime EventTimeArt
);
