using MarketDataExcelUpdater.Core.Configuration;

namespace MarketDataExcelUpdater.Core.Abstractions;

/// <summary>
/// Abstraction for a market data feed that produces quotes into the pipeline.
/// </summary>
public interface IMarketDataFeed : IAsyncDisposable
{
    string Name { get; }
    Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Feed mode used to select implementation.
/// </summary>
public enum FeedMode
{
    Demo = 0,
    Real = 1
}