using Microsoft.Extensions.Logging;

namespace MarketDataExcelUpdater.Core.Configuration;

/// <summary>
/// Minimal bootstrap helper used by Phase F main composition to acquire validated configuration
/// and emit a concise startup log. Keeps startup centralized and testable.
/// </summary>
public static class ConfigurationBootstrap
{
    public static AppConfiguration LoadWithLogging(ILogger? logger = null, string? jsonPath = null, string[]? args = null)
    {
        AppConfiguration config;
        try
        {
            config = ConfigurationLoader.Load(jsonPath: jsonPath, optional: true, diagnostics: msg => logger?.LogInformation("[config] {Message}", msg), args: args);
        }
        catch (ConfigurationException ex)
        {
            logger?.LogCritical("Configuration failed: {Errors}", string.Join("; ", ex.Errors));
            throw;
        }

        logger?.LogInformation("Configuration summary: {Summary}", BuildSummary(config));
        return config;
    }

    private static string BuildSummary(AppConfiguration c) =>
        $"Excel={c.ExcelFilePath}; Sheet={c.WorksheetName}; Stale={c.StaleSeconds}s; BatchHW={c.BatchHighWatermark}; MaxAge={c.BatchMaxAgeMs}ms; Priority=[{string.Join(',', c.PrioritySymbols)}]";
}
