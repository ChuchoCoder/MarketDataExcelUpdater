using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;

namespace MarketDataExcelUpdater.Infrastructure.Feeds;

public sealed class DemoMarketDataFeed : IMarketDataFeed
{
    private readonly TickDispatcher _dispatcher;
    private readonly FlushOrchestrator _orchestrator;
    private readonly ILogger<DemoMarketDataFeed> _logger;
    private readonly string[] _symbols;
    private Task? _loopTask;
    private readonly Random _rand = new();
    private readonly Dictionary<string, long> _sequences;

    public DemoMarketDataFeed(string[] symbols, TickDispatcher dispatcher, FlushOrchestrator orchestrator, ILogger<DemoMarketDataFeed> logger)
    {
        _symbols = symbols;
        _dispatcher = dispatcher;
        _orchestrator = orchestrator;
        _logger = logger;
        _sequences = symbols.ToDictionary(s => s, _ => 0L);
    }

    public string Name => "Demo";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loopTask != null) return Task.CompletedTask;
        _loopTask = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
        _logger.LogInformation("Demo feed started for {Count} symbols", _symbols.Length);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var symbol in _symbols)
            {
                if (ct.IsCancellationRequested) break;
                var seq = ++_sequences[symbol];
                var basePrice = 100m + (decimal)_rand.NextDouble() * 20m;
                var spread = (decimal)_rand.NextDouble();
                var quote = new Quote(
                    Bid: basePrice - spread,
                    BidSize: _rand.Next(1, 100),
                    Ask: basePrice + spread,
                    AskSize: _rand.Next(1, 100),
                    Last: basePrice,
                    Change: (decimal)_rand.NextDouble() - 0.5m,
                    Open: basePrice - 1,
                    High: basePrice + 1,
                    Low: basePrice - 2,
                    PreviousClose: basePrice - 0.5m,
                    Turnover: (decimal)_rand.Next(1000, 100000),
                    Volume: _rand.Next(100, 10000),
                    Operations: _rand.Next(1, 500),
                    EventTimeArt: DateTime.UtcNow
                );
                
                _logger.LogDebug("Demo feed generated quote for {Symbol}: Bid={Bid}, Ask={Ask}, Last={Last}, Seq={Seq}", 
                    symbol, quote.Bid, quote.Ask, quote.Last, seq);
                    
                _dispatcher.ProcessQuote(quote, symbol, seq);
                _orchestrator.OnQuoteProcessed(quote);
            }
            try
            {
                await Task.Delay(_rand.Next(180, 400), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        _logger.LogInformation("Demo feed stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopTask == null) return;
        try
        {
            await Task.WhenAny(_loopTask, Task.Delay(500));
        }
        catch { /* ignore */ }
    }
}