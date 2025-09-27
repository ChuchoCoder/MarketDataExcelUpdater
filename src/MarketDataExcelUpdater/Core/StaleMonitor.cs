using MarketDataExcelUpdater.Core.Abstractions;
using System.Collections.Concurrent;

namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Tracks quote freshness per instrument and raises transitions for stale/fresh states.
/// Thread-safe implementation using concurrent collections.
/// </summary>
public sealed class StaleMonitor : IStaleMonitor
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new();
    private readonly HashSet<string> _stale = new();
    private readonly HashSet<string> _recovered = new();
    private readonly object _stateLock = new();

    public void Observe(string symbol, DateTimeOffset exchangeTimestamp)
    {
        _lastSeen.AddOrUpdate(symbol, exchangeTimestamp, (_, _) => exchangeTimestamp);
        
        // Check if this symbol was previously stale and mark for recovery
        lock (_stateLock)
        {
            if (_stale.Remove(symbol))
            {
                _recovered.Add(symbol);
            }
        }
    }

    public IReadOnlyCollection<string> ConsumeNewlyStale(TimeSpan staleThreshold, DateTimeOffset now)
    {
        lock (_stateLock)
        {
            // Find symbols that have become stale
            foreach (var kvp in _lastSeen)
            {
                if (now - kvp.Value >= staleThreshold && !_stale.Contains(kvp.Key))
                {
                    _stale.Add(kvp.Key);
                }
            }
            
            // Return a snapshot of currently stale symbols
            return _stale.ToArray();
        }
    }

    public IReadOnlyCollection<string> ConsumeRecovered(DateTimeOffset now, TimeSpan staleThreshold)
    {
        lock (_stateLock)
        {
            // Recovery detection happens in Observe method
            var recoveredArray = _recovered.ToArray();
            _recovered.Clear();
            return recoveredArray;
        }
    }
}