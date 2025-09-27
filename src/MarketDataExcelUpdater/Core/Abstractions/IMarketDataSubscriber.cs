namespace MarketDataExcelUpdater.Core.Abstractions;

using System.Collections.Concurrent;

/// <summary>
/// Abstraction over the underlying streaming market data source.
/// Responsible only for subscription lifecycle and exposing a thread-safe queue/channel of quotes.
/// </summary>
public interface IMarketDataSubscriber : IAsyncDisposable
{
    /// <summary>Subscribe to the given instruments (idempotent for already subscribed symbols).</summary>
    ValueTask SubscribeAsync(IEnumerable<MarketInstrument> instruments, CancellationToken ct = default);

    /// <summary>Unsubscribe from the given instruments (no-op for unknown symbols).</summary>
    ValueTask UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default);

    /// <summary>Complete shutdown and resource cleanup, draining any buffers.</summary>
    ValueTask StopAsync(CancellationToken ct = default);

    /// <summary>Returns a consumer-facing async enumerable of quotes (hot). Implementation may multiplex internally.</summary>
    IAsyncEnumerable<Quote> GetQuoteStream(CancellationToken ct = default);
}
