using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace MarketDataExcelUpdater.Infrastructure.Excel;

/// <summary>
/// Factory for creating Excel writers based on configuration.
/// Uses late-bound COM for version-agnostic Excel integration.
/// </summary>
public static class ExcelWriterFactory
{
    /// <summary>
    /// Create an Excel writer instance based on the configured type.
    /// </summary>
    /// <param name="writerType">Type of Excel writer to create</param>
    /// <param name="loggerFactory">Logger factory to create typed loggers</param>
    /// <returns>Configured Excel writer instance</returns>
    /// <exception cref="ArgumentOutOfRangeException">When writerType is not supported</exception>
    public static IExcelWriter CreateWriter(ExcelWriterType writerType, ILoggerFactory loggerFactory)
    {
        return writerType switch
        {
            ExcelWriterType.LateBound => new ExcelLateBoundWriter(loggerFactory.CreateLogger<ExcelLateBoundWriter>()),
            _ => throw new ArgumentOutOfRangeException(nameof(writerType), writerType, "Unsupported Excel writer type. Only LateBound is supported.")
        };
    }

    /// <summary>
    /// Get a description of the Excel writer type for logging and diagnostics.
    /// </summary>
    /// <param name="writerType">Type of Excel writer</param>
    /// <returns>Human-readable description</returns>
    public static string GetWriterDescription(ExcelWriterType writerType)
    {
        return writerType switch
        {
            ExcelWriterType.LateBound => "Late Bound COM (live updates, version agnostic)",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Check if the specified writer type requires Excel to be closed during updates.
    /// </summary>
    /// <param name="writerType">Type of Excel writer</param>
    /// <returns>True if Excel must be closed, false if it can remain open</returns>
    public static bool RequiresClosedExcel(ExcelWriterType writerType)
    {
        return writerType switch
        {
            ExcelWriterType.LateBound => false,
            _ => true // Default to safer assumption
        };
    }
}