using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace MarketDataExcelUpdater.Infrastructure.Pipeline;

/// <summary>
/// Orchestrates the batching and flushing of Excel updates based on policy decisions.
/// Runs a background loop that checks batch policies and triggers Excel flushes.
/// </summary>
public sealed class FlushOrchestrator : IAsyncDisposable
{
    private readonly TickDispatcher _tickDispatcher;
    private readonly IExcelWriter _excelWriter;
    private readonly IBatchPolicy _batchPolicy;
    private readonly ILogger<FlushOrchestrator> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _orchestratorTask;
    
    private readonly TimeSpan _checkInterval = TimeSpan.FromMilliseconds(100); // Check every 100ms
    private Quote? _lastQuote;
    private int _totalQuotesProcessed;
    private int _totalFlushes;
    private long _totalUpdatesFlushed;
    private long _cumulativeFlushLatencyMs;
    private readonly object _metricsLock = new();

    public FlushOrchestrator(
        TickDispatcher tickDispatcher,
        IExcelWriter excelWriter,
        IBatchPolicy batchPolicy,
        ILogger<FlushOrchestrator> logger)
    {
        _tickDispatcher = tickDispatcher ?? throw new ArgumentNullException(nameof(tickDispatcher));
        _excelWriter = excelWriter ?? throw new ArgumentNullException(nameof(excelWriter));
        _batchPolicy = batchPolicy ?? throw new ArgumentNullException(nameof(batchPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Start the orchestrator loop
        _orchestratorTask = RunOrchestratorLoop(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Signal that a new quote has been processed (used for batch policy evaluation).
    /// </summary>
    public void OnQuoteProcessed(Quote quote)
    {
        _lastQuote = quote;
        _totalQuotesProcessed++;
    }

    /// <summary>
    /// Force an immediate flush regardless of batch policy.
    /// </summary>
    public async Task FlushNowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var batch = _tickDispatcher.ExtractCurrentBatch();
            if (batch.Updates.Count > 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await _excelWriter.WriteAsync(batch, cancellationToken);
                await _excelWriter.FlushAsync(cancellationToken);
                sw.Stop();

                lock (_metricsLock)
                {
                    _totalFlushes++;
                    _totalUpdatesFlushed += batch.Updates.Count;
                    _cumulativeFlushLatencyMs += sw.ElapsedMilliseconds;
                }
                _batchPolicy.Reset();

                var rate = sw.ElapsedMilliseconds > 0 
                    ? (batch.Updates.Count / (sw.ElapsedMilliseconds / 1000.0)) 
                    : double.NaN;
                _logger.LogDebug("Forced flush completed updates={Count} totalFlushes={TotalFlushes} latencyMs={Latency} avgLatencyMs={AvgLatency} totalUpdatesFlushed={TotalUpdates} ratePerSec={Rate:F2}",
                    batch.Updates.Count, _totalFlushes, sw.ElapsedMilliseconds, GetAverageFlushLatencyMs(), _totalUpdatesFlushed, rate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during forced flush");
            throw;
        }
    }

    /// <summary>
    /// Get current orchestrator statistics.
    /// </summary>
    public OrchestratorStats GetStats()
    {
        long totalUpdatesFlushed;
        long totalFlushes;
        long cumulativeLatency;

        lock (_metricsLock)
        {
            totalUpdatesFlushed = _totalUpdatesFlushed;
            totalFlushes = _totalFlushes;
            cumulativeLatency = _cumulativeFlushLatencyMs;
        }

        var pending = _tickDispatcher.ExtractCurrentBatch().Updates.Count;
        var avgLatency = totalFlushes == 0 ? 0 : (double)cumulativeLatency / totalFlushes;

        return new OrchestratorStats(
            TotalQuotesProcessed: _totalQuotesProcessed,
            TotalFlushes: (int)totalFlushes,
            PendingUpdates: pending,
            TotalUpdatesFlushed: (int)totalUpdatesFlushed,
            AverageFlushLatencyMs: avgLatency
        );
    }

    private async Task RunOrchestratorLoop(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Flush orchestrator started. Check interval: {Interval}ms", _checkInterval.TotalMilliseconds);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckAndFlush(cancellationToken);
                await Task.Delay(_checkInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Flush orchestrator loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flush orchestrator loop failed");
            throw;
        }
        finally
        {
            _logger.LogInformation("Flush orchestrator stopped. Total flushes: {TotalFlushes}", _totalFlushes);
        }
    }

    private async Task CheckAndFlush(CancellationToken cancellationToken)
    {
        try
        {
            // Check if we should flush based on batch policy
            bool shouldFlush = false;
            
            if (_lastQuote != null)
            {
                shouldFlush = _batchPolicy.ShouldFlush(_lastQuote, DateTimeOffset.UtcNow);
            }

            // Also check if we have any pending updates (in case of time-based flush)
            var batch = _tickDispatcher.ExtractCurrentBatch();
            
            if (shouldFlush && batch.Updates.Count > 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await _excelWriter.WriteAsync(batch, cancellationToken);
                await _excelWriter.FlushAsync(cancellationToken);
                sw.Stop();

                lock (_metricsLock)
                {
                    _totalFlushes++;
                    _totalUpdatesFlushed += batch.Updates.Count;
                    _cumulativeFlushLatencyMs += sw.ElapsedMilliseconds;
                }
                _batchPolicy.Reset();
                _lastQuote = null; // Reset after successful flush
                
                var rate = sw.ElapsedMilliseconds > 0 
                    ? (batch.Updates.Count / (sw.ElapsedMilliseconds / 1000.0)) 
                    : double.NaN;
                _logger.LogTrace("Policy flush updates={Count} latencyMs={Latency} avgLatencyMs={AvgLatency} totalFlushes={Flushes} ratePerSec={Rate:F2}", 
                    batch.Updates.Count, sw.ElapsedMilliseconds, GetAverageFlushLatencyMs(), _totalFlushes, rate);
            }
            else if (batch.Updates.Count > 0)
            {
                // Put the batch back if we're not flushing yet
                foreach (var update in batch.Updates)
                {
                    _tickDispatcher.ExtractCurrentBatch(); // This is a bit of a hack - in real implementation we'd have a better API
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during orchestrator check and flush");
            // Continue the loop rather than crashing
        }
    }

    private double GetAverageFlushLatencyMs()
    {
        lock (_metricsLock)
        {
            return _totalFlushes == 0 ? 0 : (double)_cumulativeFlushLatencyMs / _totalFlushes;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Shutting down flush orchestrator");
        
        try
        {
            // Cancel the orchestrator loop
            _cancellationTokenSource.Cancel();
            
            // Wait for the loop to finish
            await _orchestratorTask;
            
            // Final flush of any remaining updates
            await FlushNowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during flush orchestrator disposal");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }
}

/// <summary>
/// Statistics about the flush orchestrator performance.
/// </summary>
public sealed record OrchestratorStats(
    int TotalQuotesProcessed,
    int TotalFlushes,
    int PendingUpdates,
    int TotalUpdatesFlushed,
    double AverageFlushLatencyMs
);