using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;

namespace MarketDataExcelUpdater.Infrastructure.Feeds;

/// <summary>
/// Placeholder for a real market data feed adapter. Implements structure for future integration
/// (e.g., WebSocket / REST hybrid). Currently logs stub messages and validates required config.
/// </summary>
public sealed class RealFeedAdapter : IMarketDataFeed
{
    private readonly AppConfiguration _config;
    private readonly TickDispatcher _dispatcher;
    private readonly FlushOrchestrator _orchestrator;
    private readonly ILogger<RealFeedAdapter> _logger;
    private Task? _runTask;

    public RealFeedAdapter(AppConfiguration config, TickDispatcher dispatcher, FlushOrchestrator orchestrator, ILogger<RealFeedAdapter> logger)
    {
        _config = config;
        _dispatcher = dispatcher;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public string Name => "Real";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_runTask != null) return Task.CompletedTask;
        _runTask = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Basic validation re-run defensively
        if (string.IsNullOrWhiteSpace(_config.RealFeedEndpoint) || string.IsNullOrWhiteSpace(_config.RealFeedApiKey))
        {
            _logger.LogError("Real feed configuration incomplete. Endpoint or API key missing. Adapter idle.");
            return;
        }

        _logger.LogInformation("Real feed adapter started (endpoint={Endpoint}). This is currently a stub.", _config.RealFeedEndpoint);
        _logger.LogInformation("TODO: Implement connection, subscription management, message parsing, gap detection.");

        // Simulate idle loop until cancellation
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("Real feed adapter stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_runTask == null) return;
        try
        {
            await Task.WhenAny(_runTask, Task.Delay(500));
        }
        catch { }
    }
}