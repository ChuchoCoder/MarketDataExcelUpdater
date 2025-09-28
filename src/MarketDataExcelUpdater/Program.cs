using MarketDataExcelUpdater.Core;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Infrastructure.Excel;
using MarketDataExcelUpdater.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging;
using MarketDataExcelUpdater.Infrastructure.Feeds;

// Phase F: Main application composition root.
// Lightweight manual wiring (no Generic Host) to keep startup transparent and minimal.

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true;
	Console.WriteLine("\nCancellation requested. Shutting down...");
	var bootstrapLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("CancelHandler");
	bootstrapLogger.LogWarning("Ctrl+C detected - cancelling application");
	cts.Cancel();
};

// Temporary bootstrap logger (Information) until config is loaded
using var bootstrapLoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information).AddConsole());
var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Bootstrap");

AppConfiguration config;
try
{
	// 1. Load configuration (bootstrap logger for diagnostics)
	config = ConfigurationBootstrap.LoadWithLogging(bootstrapLoggerFactory.CreateLogger("Config"), args: args);

	// 1b. Rebuild logger factory with configured minimum level and namespace-specific overrides
	var configuredLoggerFactory = LoggerFactory.Create(builder =>
	{
		builder
			.SetMinimumLevel(config.LogLevel)
			.AddConsole();
		
		// Support namespace-specific debug logging via environment variable
		var mdxDebugLevel = Environment.GetEnvironmentVariable("Logging__LogLevel__MarketDataExcelUpdater");
		if (!string.IsNullOrEmpty(mdxDebugLevel) && Enum.TryParse<LogLevel>(mdxDebugLevel, out var debugLevel))
		{
			builder.AddFilter("MarketDataExcelUpdater", debugLevel);
		}
	});

	// Replace bootstrap factory with configured one (dispose bootstrap later via using)
	var loggerFactory = configuredLoggerFactory; // local variable overshadowing
	var logger = loggerFactory.CreateLogger("Startup");

	// 2. Instantiate core components
	var staleMonitor = new StaleMonitor();
	var batchPolicy = new BatchPolicy(config.BatchHighWatermark, TimeSpan.FromMilliseconds(config.BatchMaxAgeMs));
	var dispatcher = new TickDispatcher(staleMonitor, loggerFactory.CreateLogger<TickDispatcher>());
	
	// Create Excel writer using factory based on configuration
	var excelWriter = ExcelWriterFactory.CreateWriter(config.ExcelWriterType, loggerFactory);
	logger.LogInformation("Using Excel writer: {WriterType} - {Description}", 
		config.ExcelWriterType, 
		ExcelWriterFactory.GetWriterDescription(config.ExcelWriterType));
	
	if (ExcelWriterFactory.RequiresClosedExcel(config.ExcelWriterType))
	{
		logger.LogInformation("Note: Excel file must be closed for updates to work with {WriterType}", config.ExcelWriterType);
	}
	else
	{
		logger.LogInformation("Excel file can remain open during updates with {WriterType}", config.ExcelWriterType);
	}

	// 3. Open workbook
	// Initialize the Excel writer (LateBound implementation)
	if (excelWriter is ExcelLateBoundWriter lateBoundWriter)
	{
		await lateBoundWriter.CreateOrOpenWorkbookAsync(config.ExcelFilePath, cts.Token);
	}
	else
	{
		throw new InvalidOperationException($"Unknown Excel writer type: {excelWriter.GetType().Name}");
	}

	// 4. Start orchestrator (begins background loop immediately)
	await using var orchestrator = new FlushOrchestrator(
		dispatcher,
		excelWriter,
		batchPolicy,
		loggerFactory.CreateLogger<FlushOrchestrator>()
	);

	// 5. Select and start feed implementation
	IMarketDataFeed? feed = null;
	var legacyDisableDemo = string.Equals(Environment.GetEnvironmentVariable("MDX_DISABLE_DEMO"), "1", StringComparison.OrdinalIgnoreCase);
	var symbols = (config.PrioritySymbols.Length > 0 ? config.PrioritySymbols : new[] { "YPFD", "GGAL", "PAMP" }).Distinct().ToArray();

	switch (config.FeedMode)
	{
		case FeedMode.Demo:
			if (legacyDisableDemo)
			{
				logger.LogWarning("FeedMode=Demo but MDX_DISABLE_DEMO=1 set. No feed will be started.");
			}
			else
			{
				feed = new DemoMarketDataFeed(symbols, dispatcher, orchestrator, loggerFactory.CreateLogger<DemoMarketDataFeed>());
			}
			break;
		case FeedMode.Real:
			// Select concrete real provider
			if (config.RealFeedProvider == RealFeedProvider.Primary)
			{
				feed = new PrimaryNetFeedAdapter(config, dispatcher, orchestrator, loggerFactory.CreateLogger<PrimaryNetFeedAdapter>());
			}
			else
			{
				feed = new RealFeedAdapter(config, dispatcher, orchestrator, loggerFactory.CreateLogger<RealFeedAdapter>());
				logger.LogWarning("RealFeedProvider={Provider} not specifically implemented. Using generic RealFeedAdapter stub.", config.RealFeedProvider);
			}
			break;
		default:
			logger.LogError("Unknown FeedMode {Mode}. No feed started.", config.FeedMode);
			break;
	}

	if (feed != null)
	{
		await feed.StartAsync(cts.Token);
		logger.LogInformation("Feed {FeedName} started (mode={Mode}).", feed.Name, config.FeedMode);
	}

	// 6. Heartbeat timer
	using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
	var heartbeatTask = Task.Run(async () =>
	{
		var hbLogger = loggerFactory.CreateLogger("Heartbeat");
		while (await heartbeatTimer.WaitForNextTickAsync(cts.Token))
		{
			try
			{
				var instruments = dispatcher.GetInstruments();
				var totalGaps = instruments.Values.Sum(i => i.GapCount);
				dispatcher.QueueHeartbeatUpdate(DateTime.UtcNow, instruments.Count, totalGaps, instruments.Values.Count(i => i.Stale));
			}
			catch (OperationCanceledException) { }
			catch (Exception ex)
			{
				hbLogger.LogError(ex, "Heartbeat failure");
			}
		}
	}, cts.Token);

	logger.LogInformation("Application started. Press Ctrl+C to exit. LogLevel={LogLevel}", config.LogLevel);

	// 7. Await cancellation
	logger.LogDebug("Entering main application loop. CancellationRequested: {IsRequested}", cts.IsCancellationRequested);
	
	while (!cts.IsCancellationRequested)
	{
		try
		{
			await Task.Delay(200, cts.Token);
		}
		catch (OperationCanceledException)
		{
			logger.LogDebug("Task.Delay cancelled - exiting main loop");
			break;
		}
	}
	
	logger.LogDebug("Exited main application loop. CancellationRequested: {IsRequested}", cts.IsCancellationRequested);

	// 8. Graceful shutdown handled by using/await using disposals
	logger.LogInformation("Shutdown complete.");
	return 0;
}
catch (ConfigurationException cex)
{
	bootstrapLogger.LogCritical("Configuration error: {Errors}", string.Join("; ", cex.Errors));
	return 2;
}
catch (OperationCanceledException)
{
	bootstrapLogger.LogInformation("Cancelled.");
	return 0;
}
catch (Exception ex)
{
	bootstrapLogger.LogCritical(ex, "Fatal error during startup");
	return 1;
}

	// No local feed functions now; feed implementations encapsulate behavior.
