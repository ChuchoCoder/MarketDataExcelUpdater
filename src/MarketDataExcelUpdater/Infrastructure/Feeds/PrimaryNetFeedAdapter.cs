using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using Primary;
using Primary.Data;
using Primary.WebSockets;

namespace MarketDataExcelUpdater.Infrastructure.Feeds;

/// <summary>
/// Primary.Net feed adapter using direct Primary.Net types for stable integration.
/// </summary>
public sealed class PrimaryNetFeedAdapter : IMarketDataFeed
{
    private readonly AppConfiguration _config;
    private readonly TickDispatcher _dispatcher;
    private readonly FlushOrchestrator _orchestrator;
    private readonly ILogger<PrimaryNetFeedAdapter> _logger;
    private Task? _runTask;
    private readonly string[] _symbols;
    private readonly TimeSpan _reconnectBase = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _reconnectMax = TimeSpan.FromSeconds(30);
    private Api? _api;
    private MarketDataWebSocket? _socket;
    private readonly HttpClient _httpClient;

    public PrimaryNetFeedAdapter(AppConfiguration config, TickDispatcher dispatcher, FlushOrchestrator orchestrator, ILogger<PrimaryNetFeedAdapter> logger)
    {
        _config = config;
        _dispatcher = dispatcher;
        _orchestrator = orchestrator;
        _logger = logger;
        
        // Check multiple sources for symbols in priority order:
        // 1. PrioritySymbols (MDX_PRIORITY_SYMBOLS)
        // 2. Parse from MDX_SYMBOLS if set
        // 3. Default fallback symbols
        var symbols = new List<string>();
        
        if (config.PrioritySymbols.Length > 0)
        {
            symbols.AddRange(config.PrioritySymbols);
        }
        else
        {
            // Check for MDX_SYMBOLS environment variable as fallback
            var mdxSymbols = Environment.GetEnvironmentVariable("MDX_SYMBOLS");
            if (!string.IsNullOrEmpty(mdxSymbols))
            {
                var parsed = mdxSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim())
                                      .Where(s => !string.IsNullOrEmpty(s));
                symbols.AddRange(parsed);
            }
            else
            {
                // Default fallback to full Primary symbol names
                symbols.AddRange(new[] { "MERV - XMEV - YPFD - 24hs", "MERV - XMEV - GGALX - 24hs", "MERV - XMEV - PAMP - 24hs" });
            }
        }
        
        _symbols = symbols.Distinct().ToArray();
        _logger.LogInformation("Primary adapter initialized with {Count} symbols from configuration", _symbols.Length);
        
        _httpClient = new HttpClient();
    }

    public string Name => "Primary";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_runTask != null) return;
        
        // Start the background task but await initial connection
        var tcs = new TaskCompletionSource<bool>();
        _runTask = Task.Run(async () => 
        {
            try
            {
                await RunAsync(cancellationToken, tcs);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }
        }, cancellationToken);
        
        try
        {
            // Wait for initial connection to succeed or fail
            await tcs.Task;
        }
        catch (Exception ex)
        {
            // If initial connection fails, log and rethrow so main app can handle it
            _logger.LogError(ex, "Primary feed initial connection failed");
            throw;
        }
    }

    private async Task RunAsync(CancellationToken ct, TaskCompletionSource<bool>? initialConnectionSignal = null)
    {
        if (_config.RealFeedProvider != RealFeedProvider.Primary)
        {
            _logger.LogError("PrimaryNetFeedAdapter started with provider {Provider}", _config.RealFeedProvider);
            return;
        }

        _logger.LogInformation("Primary feed adapter starting for {Count} symbols (env={Env})", _symbols.Length, _config.PrimaryEnvironment);

        if (Environment.GetEnvironmentVariable("MDX_PRIMARY_DUMP") == "1")
            TryDumpPrimaryAssemblyMetadata();

        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await ConnectAndStreamAsync(ct, attempt == 1 ? initialConnectionSignal : null);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Primary feed stream failed (attempt {Attempt})", attempt);
                
                // Signal failure on first attempt only
                if (attempt == 1)
                {
                    initialConnectionSignal?.TrySetException(ex);
                    initialConnectionSignal = null; // Prevent signaling again
                }
                
                var delay = TimeSpan.FromMilliseconds(Math.Min(_reconnectMax.TotalMilliseconds, _reconnectBase.TotalMilliseconds * Math.Pow(2, attempt - 1)));
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Primary feed adapter stopped.");
    }

    private void TryDumpPrimaryAssemblyMetadata()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name!.StartsWith("Primary", StringComparison.OrdinalIgnoreCase));
            foreach (var a in assemblies)
                _logger.LogInformation("Primary assembly loaded: {Asm}", a.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to enumerate Primary assemblies");
        }
    }

    private async Task ConnectAndStreamAsync(CancellationToken ct, TaskCompletionSource<bool>? initialConnectionSignal = null)
    {
        _logger.LogInformation("Establishing Primary API session...");

        var endpoint = _config.RealFeedEndpoint ?? "https://api.cocos.xoms.com.ar";
        var baseUri = new Uri(endpoint);

        // Create Primary API instance directly
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _api = new Api(baseUri, _httpClient, loggerFactory);

        var username = _config.PrimaryUsername ?? throw new InvalidOperationException("PrimaryUsername is required");
        var password = _config.PrimaryPassword ?? throw new InvalidOperationException("PrimaryPassword is required");

        _logger.LogInformation("Logging in to Primary API...");
        await _api.Login(username, password);
        _logger.LogInformation("Primary login successful");

        // Get all instruments and filter to configured symbols
        var allInstruments = await _api.GetAllInstruments();
        _logger.LogInformation("Retrieved {Count} instruments from Primary API", allInstruments?.Count() ?? 0);
        
        // Log first few available symbols for debugging
        if (allInstruments?.Any() == true)
        {
            var sampleSymbols = allInstruments.Take(10).Select(i => i.Symbol).ToArray();
            _logger.LogDebug("Sample available symbols: {Symbols}", string.Join(", ", sampleSymbols));
        }
        
        var instrumentDict = allInstruments?.ToDictionary(i => i.Symbol, i => i, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, Instrument>();
        var selectedInstruments = new List<Instrument>();
        
        _logger.LogInformation("Looking for configured symbols: {ConfiguredSymbols}", string.Join(", ", _symbols));
        
        foreach (var sym in _symbols)
        {
            if (instrumentDict.TryGetValue(sym, out var inst)) 
            {
                selectedInstruments.Add(inst);
                _logger.LogInformation("Found symbol {Symbol} -> {InstrumentName}", sym, inst.Symbol);
            }
            else 
            {
                _logger.LogWarning("Configured symbol {Symbol} not found. Checking for similar symbols...", sym);
                
                // Try to find similar symbols for helpful suggestions
                if (allInstruments != null)
                {
                    var similarSymbols = allInstruments
                        .Where(i => i.Symbol.Contains(sym, StringComparison.OrdinalIgnoreCase) || 
                                   sym.Contains(i.Symbol, StringComparison.OrdinalIgnoreCase))
                        .Take(3)
                        .Select(i => i.Symbol)
                        .ToArray();
                    
                    if (similarSymbols.Any())
                    {
                        _logger.LogInformation("Similar symbols found: {SimilarSymbols}", string.Join(", ", similarSymbols));
                    }
                }
            }
        }
        
        if (selectedInstruments.Count == 0) 
            throw new InvalidOperationException("No configured symbols resolved to Primary instruments");

        // Create market data socket with direct Entry enum usage
        var entries = new[] { Entry.Bids, Entry.Offers };
        _logger.LogInformation("Creating market data socket for {Count} instruments with entries: {Entries}", 
            selectedInstruments.Count, string.Join(", ", entries));
        
        _socket = _api.CreateMarketDataSocket(selectedInstruments, entries, 1, 1);
        _logger.LogInformation("Market data socket created successfully");

        // Attach event handler directly
        _socket.OnData = OnPrimaryData;
        _logger.LogInformation("OnData event handler attached");

        _logger.LogInformation("Starting Primary market data socket...");
        
        // Signal that initial connection setup is complete
        initialConnectionSignal?.TrySetResult(true);
        
        using var reg = ct.Register(() => DisposeSocketQuiet());
        var socketTask = _socket.Start();
        
        _logger.LogInformation("Socket.Start() called, awaiting task completion...");
        _logger.LogDebug("Socket task status: {Status}, IsCompleted: {IsCompleted}", socketTask.Status, socketTask.IsCompleted);
        
        try
        {
            await socketTask; // if completes unexpectedly, trigger reconnect logic by throwing
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Socket task failed with exception");
            throw;
        }
        
        _logger.LogWarning("Socket task completed unexpectedly - this should not happen for a persistent connection");
        throw new Exception("Primary market data socket ended unexpectedly (reconnect loop)");
    }

    private void OnPrimaryData(Api api, MarketData marketData)
    {
        try 
        { 
            _logger.LogInformation("=== MARKET DATA EVENT RECEIVED ===");
            
            if (marketData == null)
            {
                _logger.LogWarning("Received null market data");
                return;
            }
            
            _logger.LogInformation("Market data for symbol: {Symbol}, InstrumentId: {InstrumentId}, Data: {DataType}", 
                marketData.InstrumentId?.Symbol ?? "unknown",
                marketData.InstrumentId?.ToString() ?? "null", 
                marketData.Data?.GetType()?.Name ?? "null");
                
            _logger.LogInformation("Market data properties: Timestamp={Timestamp}, Data={Data}",
                marketData.Timestamp, marketData.Data?.ToString() ?? "null");
            
            HandleMarketData(marketData); 
        } 
        catch (Exception ex) 
        { 
            _logger.LogError(ex, "Error in OnPrimaryData for symbol: {Symbol}", marketData?.InstrumentId?.Symbol ?? "unknown"); 
        }
    }

    private void HandleMarketData(MarketData marketData)
    {
        if (marketData == null) return;
        try
        {
            var symbol = marketData.InstrumentId.Symbol;
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(marketData.Timestamp).UtcDateTime;

            _logger.LogInformation("Processing market data for {Symbol} at {Timestamp}", symbol, timestamp);

            decimal? bid = null, bidSize = null, ask = null, askSize = null;
            
            // Access bid/offer data from the Entries Data property
            if (marketData.Data != null)
            {
                _logger.LogInformation("Market data Data type: {DataType}, Value: {DataValue}", 
                    marketData.Data.GetType().FullName, marketData.Data.ToString());
                
                try
                {
                    // Access the Entries structure - we know it has Bids, Offers, Last, etc.
                    var entries = marketData.Data;
                    var dataType = entries.GetType();
                    
                    // Try to extract Bids and Offers
                    var bidsProperty = dataType.GetProperty("Bids");
                    var offersProperty = dataType.GetProperty("Offers");
                    var lastProperty = dataType.GetProperty("Last");
                    
                    if (bidsProperty != null)
                    {
                        var bids = bidsProperty.GetValue(entries);
                        _logger.LogDebug("Bids value: {Bids} (Type: {Type})", 
                            bids?.ToString() ?? "null", bids?.GetType()?.Name ?? "null");
                            
                        // If bids is a collection, try to get the best bid
                        if (bids != null)
                        {
                            // Handle different possible bid structures
                            if (bids is IEnumerable<object> bidList)
                            {
                                var firstBid = bidList.FirstOrDefault();
                                if (firstBid != null)
                                {
                                    // Try to extract price and size from bid entry
                                    var bidType = firstBid.GetType();
                                    var priceProperty = bidType.GetProperty("Price") ?? bidType.GetProperty("price");
                                    var sizeProperty = bidType.GetProperty("Size") ?? bidType.GetProperty("size") ?? bidType.GetProperty("Quantity");
                                    
                                    if (priceProperty != null)
                                    {
                                        var priceValue = priceProperty.GetValue(firstBid);
                                        if (decimal.TryParse(priceValue?.ToString(), out var parsedPrice))
                                        {
                                            bid = parsedPrice;
                                        }
                                    }
                                    
                                    if (sizeProperty != null)
                                    {
                                        var sizeValue = sizeProperty.GetValue(firstBid);
                                        if (decimal.TryParse(sizeValue?.ToString(), out var parsedSize))
                                        {
                                            bidSize = parsedSize;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    if (offersProperty != null)
                    {
                        var offers = offersProperty.GetValue(entries);
                        _logger.LogDebug("Offers value: {Offers} (Type: {Type})", 
                            offers?.ToString() ?? "null", offers?.GetType()?.Name ?? "null");
                            
                        // If offers is a collection, try to get the best offer
                        if (offers != null)
                        {
                            if (offers is IEnumerable<object> offerList)
                            {
                                var firstOffer = offerList.FirstOrDefault();
                                if (firstOffer != null)
                                {
                                    var offerType = firstOffer.GetType();
                                    var priceProperty = offerType.GetProperty("Price") ?? offerType.GetProperty("price");
                                    var sizeProperty = offerType.GetProperty("Size") ?? offerType.GetProperty("size") ?? offerType.GetProperty("Quantity");
                                    
                                    if (priceProperty != null)
                                    {
                                        var priceValue = priceProperty.GetValue(firstOffer);
                                        if (decimal.TryParse(priceValue?.ToString(), out var parsedPrice))
                                        {
                                            ask = parsedPrice;
                                        }
                                    }
                                    
                                    if (sizeProperty != null)
                                    {
                                        var sizeValue = sizeProperty.GetValue(firstOffer);
                                        if (decimal.TryParse(sizeValue?.ToString(), out var parsedSize))
                                        {
                                            askSize = parsedSize;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // Log all discovered properties for debugging
                    var properties = dataType.GetProperties().Select(p => p.Name).ToArray();
                    _logger.LogDebug("Market data properties available: {Properties}", 
                        string.Join(", ", properties));
                    
                    foreach (var prop in dataType.GetProperties().Take(5)) // Limit to first 5 to avoid spam
                    {
                        var value = prop.GetValue(entries);
                        _logger.LogDebug("Property {Name}: {Value} ({Type})", 
                            prop.Name, value?.ToString() ?? "null", value?.GetType()?.Name ?? "null");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error inspecting market data structure for {Symbol}", symbol);
                }
            }
            else
            {
                _logger.LogWarning("Market data Data property is null for {Symbol}", symbol);
            }

            var quote = new Quote(
                bid, bidSize,
                ask, askSize,
                null, null, null, null, null, null, null, null, null,
                timestamp
            );

            _logger.LogInformation("Created quote for {Symbol}: Bid={Bid}, Ask={Ask}, BidSize={BidSize}, AskSize={AskSize}", 
                symbol, bid, ask, bidSize, askSize);

            _dispatcher.ProcessQuote(quote, symbol);
            _orchestrator.OnQuoteProcessed(quote);
            
            _logger.LogInformation("Quote processed and sent to orchestrator for {Symbol}", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception mapping Primary market data");
        }
    }

    private void DisposeSocketQuiet()
    {
        try
        {
            _logger.LogDebug("Disposing market data socket...");
            _socket?.Dispose();
            _logger.LogDebug("Market data socket disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing socket");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { DisposeSocketQuiet(); } catch { }
        try { _httpClient?.Dispose(); } catch { }
        if (_runTask != null)
        {
            try { await Task.WhenAny(_runTask, Task.Delay(500)); } catch { }
        }
    }
}