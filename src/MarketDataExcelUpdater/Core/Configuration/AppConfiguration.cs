using System.ComponentModel.DataAnnotations;
using MarketDataExcelUpdater.Core.Abstractions;
using System.Text.RegularExpressions;

namespace MarketDataExcelUpdater.Core.Configuration;

/// <summary>
/// Application configuration with validation (FR-014, FR-025)
/// </summary>
public sealed class AppConfiguration
{
    [Range(1, 300, ErrorMessage = "StaleSeconds must be between 1 and 300")]
    public int StaleSeconds { get; set; } = 5;

    [Range(1, 10000, ErrorMessage = "BatchHighWatermark must be between 1 and 10000")]
    public int BatchHighWatermark { get; set; } = 100;

    [Range(10, 60000, ErrorMessage = "BatchMaxAgeMs must be between 10 and 60000")]
    public int BatchMaxAgeMs { get; set; } = 1000;

    [Range(1, 3600, ErrorMessage = "SymbolScanIntervalSeconds must be between 1 and 3600")]
    public int SymbolScanIntervalSeconds { get; set; } = 30;

    [Range(1, 1000, ErrorMessage = "MaxTicksPerSymbol must be between 1 and 1000")]
    public int MaxTicksPerSymbol { get; set; } = 100;

    [Range(1, 600, ErrorMessage = "TickRetentionMinutes must be between 1 and 600")]
    public int TickRetentionMinutes { get; set; } = 5;

    [Required, MinLength(1, ErrorMessage = "ExcelFilePath is required")]
    public string ExcelFilePath { get; set; } = "MarketData.xlsx";

    [Required, MinLength(1, ErrorMessage = "WorksheetName is required")]
    public string WorksheetName { get; set; } = "MarketData";

    public string[] PrioritySymbols { get; set; } = Array.Empty<string>();

    public bool EnableReplayLogging { get; set; } = false;

    public string? ReplayLogPath { get; set; }

    /// <summary>
    /// Minimum log level applied at startup (FR-new logging enhancement). Defaults to Information.
    /// </summary>
    public Microsoft.Extensions.Logging.LogLevel LogLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Information;

    // Feed selection & real feed placeholders
    public FeedMode FeedMode { get; set; } = FeedMode.Demo;
    public string? RealFeedEndpoint { get; set; }
    public string? RealFeedApiKey { get; set; }
    public RealFeedProvider RealFeedProvider { get; set; } = RealFeedProvider.None; // When FeedMode=Real must be set
    public string? PrimaryUsername { get; set; }
    public string? PrimaryPassword { get; set; }
    public string? PrimaryAccount { get; set; }
    public PrimaryEnvironment? PrimaryEnvironment { get; set; }

    /// <summary>
    /// Excel writer implementation to use. Uses LateBound COM for version-agnostic integration.
    /// </summary>
    public ExcelWriterType ExcelWriterType { get; set; } = ExcelWriterType.LateBound;

    /// <summary>
    /// Validate the configuration and return validation results
    /// </summary>
    public ValidationResult[] Validate()
    {
        var context = new ValidationContext(this);
        var results = new List<ValidationResult>();
        
        Validator.TryValidateObject(this, context, results, validateAllProperties: true);
        
        // Custom validation logic
        if (BatchMaxAgeMs >= StaleSeconds * 1000)
        {
            results.Add(new ValidationResult(
                "BatchMaxAgeMs should be less than StaleSeconds * 1000 to ensure timely updates",
                new[] { nameof(BatchMaxAgeMs), nameof(StaleSeconds) }
            ));
        }

        if (!Path.HasExtension(ExcelFilePath) || !ExcelFilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new ValidationResult(
                "ExcelFilePath must be a .xlsx file",
                new[] { nameof(ExcelFilePath) }
            ));
        }

        // LogLevel is an enum; ensure it is defined (defensive for potential external binding)
        if (!Enum.IsDefined(typeof(Microsoft.Extensions.Logging.LogLevel), LogLevel))
        {
            results.Add(new ValidationResult(
                "LogLevel must be a valid Microsoft.Extensions.Logging.LogLevel value",
                new[] { nameof(LogLevel) }
            ));
        }

        if (!Enum.IsDefined(typeof(FeedMode), FeedMode))
        {
            results.Add(new ValidationResult(
                "FeedMode must be a valid enum value",
                new[] { nameof(FeedMode) }
            ));
        }

        if (FeedMode == FeedMode.Real)
        {
            // Generic (non-Primary) providers still require endpoint+apikey
            if (RealFeedProvider != RealFeedProvider.Primary)
            {
                if (string.IsNullOrWhiteSpace(RealFeedEndpoint))
                    results.Add(new ValidationResult("RealFeedEndpoint is required when FeedMode=Real for non-Primary providers", new[] { nameof(RealFeedEndpoint), nameof(FeedMode), nameof(RealFeedProvider) }));
                if (string.IsNullOrWhiteSpace(RealFeedApiKey))
                    results.Add(new ValidationResult("RealFeedApiKey is required when FeedMode=Real for non-Primary providers", new[] { nameof(RealFeedApiKey), nameof(FeedMode), nameof(RealFeedProvider) }));
            }

            if (RealFeedProvider == RealFeedProvider.None)
            {
                results.Add(new ValidationResult("RealFeedProvider must be specified when FeedMode=Real", new[] { nameof(RealFeedProvider), nameof(FeedMode) }));
            }

            if (RealFeedProvider == RealFeedProvider.Primary)
            {
                if (string.IsNullOrWhiteSpace(PrimaryUsername))
                    results.Add(new ValidationResult("PrimaryUsername is required for Primary provider", new[] { nameof(PrimaryUsername), nameof(RealFeedProvider) }));
                if (string.IsNullOrWhiteSpace(PrimaryPassword))
                    results.Add(new ValidationResult("PrimaryPassword is required for Primary provider", new[] { nameof(PrimaryPassword), nameof(RealFeedProvider) }));
                if (PrimaryEnvironment is null)
                    results.Add(new ValidationResult("PrimaryEnvironment is required for Primary provider", new[] { nameof(PrimaryEnvironment), nameof(RealFeedProvider) }));
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Get batch policy configuration as TimeSpan values
    /// </summary>
    public BatchPolicyConfig GetBatchPolicyConfig() => new()
    {
        HighWatermark = BatchHighWatermark,
        MaxAge = TimeSpan.FromMilliseconds(BatchMaxAgeMs),
        PrioritySymbols = PrioritySymbols.ToHashSet()
    };

    /// <summary>
    /// Get stale monitoring configuration as TimeSpan
    /// </summary>
    public TimeSpan GetStaleThreshold() => TimeSpan.FromSeconds(StaleSeconds);

    /// <summary>
    /// Get symbol scanning interval as TimeSpan
    /// </summary>
    public TimeSpan GetSymbolScanInterval() => TimeSpan.FromSeconds(SymbolScanIntervalSeconds);

    /// <summary>
    /// Get retention policy configuration
    /// </summary>
    public RetentionConfig GetRetentionConfig() => new()
    {
        MaxTicksPerSymbol = MaxTicksPerSymbol,
        RetentionTime = TimeSpan.FromMinutes(TickRetentionMinutes)
    };
}

/// <summary>
/// Batch policy configuration extracted from main config
/// </summary>
public sealed class BatchPolicyConfig
{
    public int HighWatermark { get; init; }
    public TimeSpan MaxAge { get; init; }
    public HashSet<string> PrioritySymbols { get; init; } = new();
}

/// <summary>
/// Retention policy configuration for tick data (FR-016, FR-026)
/// </summary>
public sealed class RetentionConfig
{
    public int MaxTicksPerSymbol { get; init; }
    public TimeSpan RetentionTime { get; init; }
    
    /// <summary>
    /// Determine if retention cleanup should occur based on either limit
    /// </summary>
    public bool ShouldEvict(int currentTickCount, TimeSpan oldestTickAge)
    {
        return currentTickCount > MaxTicksPerSymbol || oldestTickAge > RetentionTime;
    }
}

public enum RealFeedProvider
{
    None = 0,
    Primary = 1
}

public enum PrimaryEnvironment
{
    Test = 0,
    Prod = 1
}

public enum ExcelWriterType
{
    LateBound = 0
}