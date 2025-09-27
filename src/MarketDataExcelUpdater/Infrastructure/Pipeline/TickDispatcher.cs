using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using Microsoft.Extensions.Logging;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Core.Retention;

namespace MarketDataExcelUpdater.Infrastructure.Pipeline;

/// <summary>
/// Dispatches incoming quotes to update market instruments and queue Excel cell changes.
/// Core pipeline component that coordinates quote → model → Excel flow.
/// </summary>
public sealed class TickDispatcher
{
    private readonly Dictionary<string, MarketInstrument> _instruments = new();
    private readonly IStaleMonitor _staleMonitor;
    private readonly UpdateBatch _currentBatch = new();
    private readonly ILogger<TickDispatcher> _logger;
    private readonly ITickRetentionManager? _retentionManager;
    private RetentionMetrics? _lastRetentionMetrics;

    public TickDispatcher(IStaleMonitor staleMonitor, ILogger<TickDispatcher> logger, ITickRetentionManager? retentionManager = null)
    {
        _staleMonitor = staleMonitor ?? throw new ArgumentNullException(nameof(staleMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _retentionManager = retentionManager; // optional until wired
    }

    /// <summary>
    /// Process an incoming quote: update instrument, queue Excel changes, mark stale monitor.
    /// </summary>
    public void ProcessQuote(Quote quote, string symbol, long sequence = -1)
    {
        try
        {
            // Get or create instrument
            if (!_instruments.TryGetValue(symbol, out var instrument))
            {
                instrument = new MarketInstrument(symbol);
                _instruments[symbol] = instrument;
                _logger.LogDebug("Created new instrument for symbol {Symbol}", symbol);
            }

            // Update instrument with quote
            var updateResult = instrument.TryUpdate(quote, sequence);
            if (!updateResult.Success)
            {
                _logger.LogTrace("Quote update rejected for {Symbol}: timestamp too old", symbol);
                return;
            }

            // Log sequence issues
            if (updateResult.SequenceResult == SequenceResult.Gap)
            {
                _logger.LogWarning("Sequence gap detected for {Symbol}. Expected {Expected}, got {Actual}. Gap count now: {GapCount}", 
                    symbol, instrument.LastSequence + 1, sequence, instrument.GapCount);
            }
            else if (updateResult.SequenceResult == SequenceResult.Duplicate)
            {
                _logger.LogDebug("Duplicate sequence {Sequence} for {Symbol}", sequence, symbol);
                return;
            }

            // Mark instrument as fresh in stale monitor
            _staleMonitor.Observe(symbol, new DateTimeOffset(quote.EventTimeArt));

            // Queue Excel cell updates
            QueueCellUpdates(symbol, instrument);

            // Retention enforcement (optional until configured)
            if (_retentionManager != null)
            {
                var retention = _retentionManager.OnNewTick(symbol, instrument.LastSequence, instrument.LastUpdateTime);
                if (retention.EvictedCount > 0)
                {
                    _logger.LogDebug("Retention evicted {Evicted} ticks for {Symbol}. Current={Current} TotalEvicted={Total}",
                        retention.EvictedCount, symbol, retention.CurrentCount, retention.TotalEvicted);
                }
                _lastRetentionMetrics = _retentionManager.GetMetrics();
            }

            _logger.LogTrace("Processed quote for {Symbol}: Last={Last}, Bid={Bid}, Ask={Ask}", 
                symbol, quote.Last, quote.Bid, quote.Ask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing quote for symbol {Symbol}", symbol);
            throw;
        }
    }

    /// <summary>
    /// Get all tracked instruments.
    /// </summary>
    public IReadOnlyDictionary<string, MarketInstrument> GetInstruments() => _instruments;

    /// <summary>
    /// Get the current batch of pending updates and clear it.
    /// </summary>
    public UpdateBatch ExtractCurrentBatch()
    {
        if (_currentBatch.Updates.Count == 0)
        {
            return new UpdateBatch();
        }

        var batch = new UpdateBatch(_currentBatch.Updates);
        _currentBatch.Clear();
        
        _logger.LogDebug("Extracted batch with {Count} updates", batch.Updates.Count);
        return batch;
    }

    /// <summary>
    /// Update stale flags for all instruments.
    /// </summary>
    public void UpdateStaleFlags(DateTime now)
    {
        // Note: In real implementation, stale detection would be done through the StaleMonitor
        // which tracks quote timestamps and determines staleness. For now we'll just log.
        
        var staleCount = _instruments.Values.Count(i => i.Stale);
        if (staleCount > 0)
        {
            _logger.LogDebug("Found {Count} stale instruments out of {Total}", staleCount, _instruments.Count);
        }
    }

    /// <summary>
    /// Add or update a heartbeat/metrics row with current counters.
    /// </summary>
    public void QueueHeartbeatUpdate(DateTime timestamp, int totalQuotes, int totalGaps, int staleCout)
    {
        const string metricsSheet = "Metrics";
        const int heartbeatRow = 2; // Row 2 for metrics (row 1 has headers)

        _currentBatch.Enqueue(new CellUpdate(metricsSheet, "Timestamp", heartbeatRow, timestamp), timestamp);
        _currentBatch.Enqueue(new CellUpdate(metricsSheet, "TotalQuotes", heartbeatRow, totalQuotes), timestamp);
        _currentBatch.Enqueue(new CellUpdate(metricsSheet, "TotalGaps", heartbeatRow, totalGaps), timestamp);
        _currentBatch.Enqueue(new CellUpdate(metricsSheet, "StaleCount", heartbeatRow, staleCout), timestamp);
        _currentBatch.Enqueue(new CellUpdate(metricsSheet, "InstrumentCount", heartbeatRow, _instruments.Count), timestamp);

        if (_lastRetentionMetrics is { } r)
        {
            _currentBatch.Enqueue(new CellUpdate(metricsSheet, "RetentionTotalEvicted", heartbeatRow, r.TotalEvicted), timestamp);
            if (r.LastEvictionUtc.HasValue)
            {
                _currentBatch.Enqueue(new CellUpdate(metricsSheet, "RetentionLastEvictionUtc", heartbeatRow, r.LastEvictionUtc.Value), timestamp);
                _currentBatch.Enqueue(new CellUpdate(metricsSheet, "RetentionLastBatchEvicted", heartbeatRow, r.LastEvictedBatchCount), timestamp);
            }
        }

        _logger.LogTrace("Queued heartbeat update: Quotes={Quotes}, Gaps={Gaps}, Stale={Stale}, Instruments={Instruments}",
            totalQuotes, totalGaps, staleCout, _instruments.Count);
    }

    private void QueueCellUpdates(string symbol, MarketInstrument instrument)
    {
        const string marketDataSheet = "MarketData";
        var now = DateTime.UtcNow;
        
        // Find or assign a row for this symbol (we'll use a simple approach for now)
        var row = GetRowForSymbol(symbol);

        var quote = instrument.LastQuote!;
        
        // Queue basic market data updates
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Symbol", row, symbol), now);
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "LastUpdate", row, instrument.LastUpdateTime), now);
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "IsStale", row, instrument.Stale), now);
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "GapCount", row, instrument.GapCount), now);
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Sequence", row, instrument.LastSequence), now);

        // Queue quote data (null values will be handled by Excel writer)
        if (quote.Last.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Last", row, quote.Last.Value), now);
        
        if (quote.Bid.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Bid", row, quote.Bid.Value), now);
            
        if (quote.Ask.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Ask", row, quote.Ask.Value), now);
            
        if (quote.BidSize.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "BidSize", row, quote.BidSize.Value), now);
            
        if (quote.AskSize.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "AskSize", row, quote.AskSize.Value), now);
            
        if (quote.Volume.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Volume", row, quote.Volume.Value), now);
            
        if (quote.Change.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Change", row, quote.Change.Value), now);
            
        if (quote.Open.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Open", row, quote.Open.Value), now);
            
        if (quote.High.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "High", row, quote.High.Value), now);
            
        if (quote.Low.HasValue)
            _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "Low", row, quote.Low.Value), now);
    }

    private void QueueStaleUpdate(string symbol, bool isStale)
    {
        const string marketDataSheet = "MarketData";
        var row = GetRowForSymbol(symbol);
        
        _currentBatch.Enqueue(new CellUpdate(marketDataSheet, "IsStale", row, isStale), DateTime.UtcNow);
    }

    private int GetRowForSymbol(string symbol)
    {
        // Simple row assignment: symbols get consecutive rows starting from row 2 (row 1 for headers)
        // In a real implementation, this might use a more sophisticated mapping
        var symbols = _instruments.Keys.OrderBy(s => s).ToList();
        var index = symbols.IndexOf(symbol);
        return index + 2; // Row 1 is headers, so start at row 2
    }
}