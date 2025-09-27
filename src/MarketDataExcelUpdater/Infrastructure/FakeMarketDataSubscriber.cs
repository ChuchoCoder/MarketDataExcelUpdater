using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using System.Threading.Channels;

namespace MarketDataExcelUpdater.Infrastructure;

/// <summary>
/// Fake/simulated implementation of IMarketDataSubscriber for testing and development.
/// Generates synthetic market data events for subscribed instruments.
/// </summary>
public sealed class FakeMarketDataSubscriber : IMarketDataSubscriber
{
    private readonly Dictionary<string, MarketInstrument> _subscribedInstruments = new();
    private readonly Channel<Quote> _quoteChannel;
    private readonly ChannelWriter<Quote> _writer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _dataGenerationTask;
    private readonly Random _random = new();

    public bool IsRunning { get; private set; } = true;
    public IReadOnlyDictionary<string, MarketInstrument> SubscribedInstruments => _subscribedInstruments;

    public FakeMarketDataSubscriber()
    {
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        };

        _quoteChannel = Channel.CreateBounded<Quote>(options);
        _writer = _quoteChannel.Writer;

        // Start background data generation
        _dataGenerationTask = Task.Run(GenerateDataAsync);
    }

    public ValueTask SubscribeAsync(IEnumerable<MarketInstrument> instruments, CancellationToken ct = default)
    {
        foreach (var instrument in instruments)
        {
            // Union semantics - idempotent for already subscribed symbols
            _subscribedInstruments.TryAdd(instrument.Symbol, instrument);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        foreach (var symbol in symbols)
        {
            _subscribedInstruments.Remove(symbol);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken ct = default)
    {
        IsRunning = false;
        _cancellationTokenSource.Cancel();
        _writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<Quote> GetQuoteStream(CancellationToken ct = default)
    {
        return _quoteChannel.Reader.ReadAllAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        if (IsRunning)
        {
            _cancellationTokenSource.Cancel();
            _writer.TryComplete();
        }

        _dataGenerationTask?.Dispose();
        _cancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task GenerateDataAsync()
    {
        var sequenceNumbers = new Dictionary<string, long>();

        try
        {
            while (IsRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (_subscribedInstruments.Any())
                {
                    // Pick a random subscribed instrument
                    var symbols = _subscribedInstruments.Keys.ToArray();
                    var symbol = symbols[_random.Next(symbols.Length)];
                    
                    // Generate sequence number
                    if (!sequenceNumbers.ContainsKey(symbol))
                        sequenceNumbers[symbol] = 0;
                    sequenceNumbers[symbol]++;

                    // Generate synthetic quote
                    var quote = GenerateSyntheticQuote();
                    
                    // Send the quote (in real implementation, quote would include symbol)
                    await _writer.WriteAsync(quote, _cancellationTokenSource.Token);
                }

                // Simulate realistic data rate - about 10 quotes per second per instrument
                await Task.Delay(100, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        finally
        {
            _writer.TryComplete();
        }
    }

    private Quote GenerateSyntheticQuote()
    {
        var basePrice = 100m + (decimal)(_random.NextDouble() * 100); // Random price 100-200
        var spread = 0.5m;

        return new Quote(
            Bid: basePrice,
            BidSize: _random.Next(100, 1000),
            Ask: basePrice + spread,
            AskSize: _random.Next(100, 1000),
            Last: basePrice + (decimal)((_random.NextDouble() - 0.5) * 2), // Random around mid
            Change: (decimal)((_random.NextDouble() - 0.5) * 4), // Random change -2 to +2
            Open: basePrice + (decimal)((_random.NextDouble() - 0.5) * 2),
            High: basePrice + (decimal)(_random.NextDouble() * 2),
            Low: basePrice - (decimal)(_random.NextDouble() * 2),
            PreviousClose: basePrice - (decimal)((_random.NextDouble() - 0.5) * 2),
            Turnover: _random.Next(10000, 100000),
            Volume: _random.Next(1000, 10000),
            Operations: _random.Next(10, 100),
            EventTimeArt: DateTime.Now
        );
    }
}