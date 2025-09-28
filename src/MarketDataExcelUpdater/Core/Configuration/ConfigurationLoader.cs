using Microsoft.Extensions.Configuration;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Core.Configuration;

/// <summary>
/// Loads <see cref="AppConfiguration"/> using .NET's built-in configuration providers.
/// Uses standard configuration priority: defaults -> appsettings.json -> environment variables -> command line args.
/// </summary>
public static class ConfigurationLoader
{
    private const string DefaultJsonFile = "appsettings.json";

    /// <summary>
    /// Load configuration using .NET's standard configuration providers.
    /// </summary>
    public static AppConfiguration Load(string? jsonPath = null, bool optional = true, Action<string>? diagnostics = null, string[]? args = null)
    {
        diagnostics ??= _ => { };
        jsonPath ??= Path.Combine(AppContext.BaseDirectory, DefaultJsonFile);

        var builder = new ConfigurationBuilder();
        
        // 1. Add JSON configuration file
        if (File.Exists(jsonPath))
        {
            diagnostics($"Loading configuration json: {jsonPath}");
            builder.AddJsonFile(jsonPath, optional: optional, reloadOnChange: false);
        }
        else if (!optional)
        {
            throw new ConfigurationException($"Configuration file '{jsonPath}' not found and not optional", new[] { "Missing configuration file" });
        }
        
        // 2. Add environment variables (standard .NET approach)
        builder.AddEnvironmentVariables();
        
        // 3. Add command line arguments if provided
        if (args != null && args.Length > 0)
        {
            builder.AddCommandLine(args);
        }

        var configuration = builder.Build();
        
        // Bind configuration to strongly-typed object
        var config = new AppConfiguration();
        try
        {
            configuration.Bind(config);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to convert configuration"))
        {
            throw new ConfigurationException("Configuration binding failed - invalid configuration values provided", new[] { ex.Message });
        }
        
        // Log environment variable overrides that were applied
        LogAppliedEnvironmentVariables(configuration, diagnostics);

        // Validate
        var validation = config.Validate();
        if (validation.Length > 0)
        {
            throw new ConfigurationException("Configuration validation failed", validation.Select(v => v.ErrorMessage ?? v.ToString()));
        }

        diagnostics($"Configuration loaded successfully. ExcelFile={config.ExcelFilePath}, Stale={config.StaleSeconds}s BatchHW={config.BatchHighWatermark}");
        return config;
    }

    private static void LogAppliedEnvironmentVariables(IConfiguration configuration, Action<string> diagnostics)
    {
        // Simple approach: check if specific environment variables are set and log them
        var propertiesToCheck = new[]
        {
            "StaleSeconds", "BatchHighWatermark", "BatchMaxAgeMs", "SymbolScanIntervalSeconds",
            "MaxTicksPerSymbol", "TickRetentionMinutes", "ExcelFilePath", "WorksheetName",
            "PrioritySymbols", "EnableReplayLogging", "ReplayLogPath", "LogLevel",
            "FeedMode", "RealFeedEndpoint", "RealFeedApiKey", "RealFeedProvider",
            "PrimaryUsername", "PrimaryPassword", "PrimaryAccount", "PrimaryEnvironment",
            "ExcelWriterType"
        };

        foreach (var property in propertiesToCheck)
        {
            var envKey = property;
            var envValue = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrEmpty(envValue))
            {
                diagnostics($"Env override: {envKey}={envValue}");
            }
        }
    }
}
