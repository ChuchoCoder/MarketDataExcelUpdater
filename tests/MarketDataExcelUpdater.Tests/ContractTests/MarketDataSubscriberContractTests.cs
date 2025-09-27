using FluentAssertions;
using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Tests.ContractTests;

/// <summary>
/// Contract tests for IMarketDataSubscriber - these should initially fail with NotImplementedException
/// </summary>
public sealed class MarketDataSubscriberContractTests
{
    [Fact]
    public void Subscribe_should_accept_instrument_enumerable()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        var instruments = new[] { new MarketInstrument("GGAL"), new MarketInstrument("YPFD") };

        var act = async () => await subscriber.SubscribeAsync(instruments);
        
        act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void Subscribe_multiple_calls_should_be_union_idempotent()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        var instruments1 = new[] { new MarketInstrument("GGAL") };
        var instruments2 = new[] { new MarketInstrument("GGAL"), new MarketInstrument("YPFD") };

        var act = async () => {
            await subscriber.SubscribeAsync(instruments1);
            await subscriber.SubscribeAsync(instruments2); // Should be idempotent for GGAL
        };
        
        act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void Unsubscribe_should_handle_unknown_symbols_gracefully()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        var unknownSymbols = new[] { "UNKNOWN_SYMBOL" };

        var act = async () => await subscriber.UnsubscribeAsync(unknownSymbols);
        
        act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void GetQuoteStream_should_return_async_enumerable()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        
        var act = () => subscriber.GetQuoteStream();
        
        act.Should().NotBeNull();
        // The enumerable itself should be returned, but iteration will throw NotImplementedException
    }

    [Fact]
    public void QuoteStream_iteration_should_eventually_throw_not_implemented()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        
        var act = async () => {
            await foreach (var quote in subscriber.GetQuoteStream())
            {
                // Should throw before we get here
                break;
            }
        };
        
        act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void StopAsync_should_gracefully_shutdown()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        
        var act = async () => await subscriber.StopAsync();
        
        act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public void DisposeAsync_should_clean_up_resources()
    {
        IMarketDataSubscriber subscriber = new NotImplementedMarketDataSubscriber();
        
        var act = async () => await subscriber.DisposeAsync();
        
        act.Should().ThrowAsync<NotImplementedException>();
    }
}

/// <summary>
/// Temporary implementation that throws NotImplementedException - to be replaced in Phase B/E
/// </summary>
internal sealed class NotImplementedMarketDataSubscriber : IMarketDataSubscriber
{
    public ValueTask DisposeAsync() => throw new NotImplementedException();
    
    public IAsyncEnumerable<Quote> GetQuoteStream(CancellationToken ct = default) => throw new NotImplementedException();
    
    public ValueTask StopAsync(CancellationToken ct = default) => throw new NotImplementedException();
    
    public ValueTask SubscribeAsync(IEnumerable<MarketInstrument> instruments, CancellationToken ct = default) => throw new NotImplementedException();
    
    public ValueTask UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default) => throw new NotImplementedException();
}