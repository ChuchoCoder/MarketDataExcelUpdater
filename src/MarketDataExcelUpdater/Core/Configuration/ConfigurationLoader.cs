using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Core.Configuration;

/// <summary>
/// Loads <see cref="AppConfiguration"/> from layered sources (defaults -> json -> env vars).
/// Command-line args hook can be added later (Phase F).
/// </summary>
public static class ConfigurationLoader
{
    private const string DefaultJsonFile = "appsettings.json";
    private const string EnvPrefix = "MDX_"; // Market Data Excel

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Load configuration using defaults + optional JSON file + environment overrides.
    /// </summary>
    public static AppConfiguration Load(string? jsonPath = null, bool optional = true, Action<string>? diagnostics = null)
    {
        diagnostics ??= _ => { };

        var config = new AppConfiguration(); // defaults already in model

        // 1. JSON file (if present or not optional)
        jsonPath ??= Path.Combine(AppContext.BaseDirectory, DefaultJsonFile);
        if (File.Exists(jsonPath))
        {
            diagnostics($"Loading configuration json: {jsonPath}");
            try
            {
                using var stream = File.OpenRead(jsonPath);
                var jsonConfig = JsonSerializer.Deserialize<AppConfiguration>(stream, JsonOptions);
                if (jsonConfig != null)
                {
                    config = Merge(config, jsonConfig);
                }
            }
            catch (Exception ex)
            {
                throw new ConfigurationException($"Failed reading configuration file '{jsonPath}'", new[] { ex.Message });
            }
        }
        else if (!optional)
        {
            throw new ConfigurationException($"Configuration file '{jsonPath}' not found and not optional", new[] { "Missing configuration file" });
        }

        // 2. Environment variable overrides
        ApplyEnvironmentOverrides(config, diagnostics);

        // 3. Validate
        var validation = config.Validate();
        if (validation.Length > 0)
        {
            throw new ConfigurationException("Configuration validation failed", validation.Select(v => v.ErrorMessage ?? v.ToString()));
        }

        diagnostics($"Configuration loaded successfully. ExcelFile={config.ExcelFilePath}, Stale={config.StaleSeconds}s BatchHW={config.BatchHighWatermark}");
        return config;
    }

    private static void ApplyEnvironmentOverrides(AppConfiguration config, Action<string> diagnostics)
    {
        // Mapping for non-trivial names where raw env key cannot be algorithmically transformed
        // because internal word boundaries are lost (e.g. STALESECONDS, HIGHWATERMARK, MAXAGEMS)
        var explicitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STALESECONDS"] = nameof(AppConfiguration.StaleSeconds),
            ["BATCH_HIGHWATERMARK"] = nameof(AppConfiguration.BatchHighWatermark),
            ["BATCH_MAXAGEMS"] = nameof(AppConfiguration.BatchMaxAgeMs),
            ["SYMBOLSCANINTERVALSECONDS"] = nameof(AppConfiguration.SymbolScanIntervalSeconds),
            ["SYMBOL_SCAN_INTERVAL_SECONDS"] = nameof(AppConfiguration.SymbolScanIntervalSeconds),
            ["MAXTICKSPERSYMBOL"] = nameof(AppConfiguration.MaxTicksPerSymbol),
            ["TICKRETENTIONMINUTES"] = nameof(AppConfiguration.TickRetentionMinutes),
            ["EXCELFILEPATH"] = nameof(AppConfiguration.ExcelFilePath),
            ["WORKSHEETNAME"] = nameof(AppConfiguration.WorksheetName),
            ["PRIORITY_SYMBOLS"] = nameof(AppConfiguration.PrioritySymbols),
            ["ENABLEREPLAYLOGGING"] = nameof(AppConfiguration.EnableReplayLogging),
            ["REPLAYLOGPATH"] = nameof(AppConfiguration.ReplayLogPath),
            ["LOGLEVEL"] = nameof(AppConfiguration.LogLevel)
            , ["FEEDMODE"] = nameof(AppConfiguration.FeedMode)
            , ["REALFEED_ENDPOINT"] = nameof(AppConfiguration.RealFeedEndpoint)
            , ["REALFEED_APIKEY"] = nameof(AppConfiguration.RealFeedApiKey)
            , ["REALFEED_PROVIDER"] = nameof(AppConfiguration.RealFeedProvider)
            , ["PRIMARY_USERNAME"] = nameof(AppConfiguration.PrimaryUsername)
            , ["PRIMARY_PASSWORD"] = nameof(AppConfiguration.PrimaryPassword)
            , ["PRIMARY_ACCOUNT"] = nameof(AppConfiguration.PrimaryAccount)
            , ["PRIMARY_ENVIRONMENT"] = nameof(AppConfiguration.PrimaryEnvironment)
            , ["EXCELWRITERTYPE"] = nameof(AppConfiguration.ExcelWriterType)
        };

        foreach (var envVar in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
        {
            if (envVar.Key is not string keyRaw || envVar.Value is not string value) continue;
            if (!keyRaw.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            var key = keyRaw.Substring(EnvPrefix.Length); // remove prefix
            string? mapped = null;
            if (explicitMap.TryGetValue(key, out var explicitName))
            {
                mapped = explicitName;
            }

            if (TryApply(config, key, value, mapped))
            {
                diagnostics($"Env override: {keyRaw}={value}");
            }
        }
    }

    private static bool TryApply(AppConfiguration target, string key, string value, string? mappedPropertyName = null)
    {
        string normalized;
        if (!string.IsNullOrEmpty(mappedPropertyName))
        {
            normalized = mappedPropertyName;
        }
        else
        {
            // Fallback normalization: UPPER_SNAKE -> PascalCase (may fail for compound collapsed words)
            normalized = string.Concat(
                key.Split('_', StringSplitOptions.RemoveEmptyEntries)
                   .Select(segment => Capitalize(segment.ToLowerInvariant()))
            );
        }

        var prop = typeof(AppConfiguration).GetProperty(normalized);
        if (prop == null || !prop.CanWrite) return false;

        try
        {
            object? converted = ConvertValue(prop.PropertyType, value);
            prop.SetValue(target, converted);
            return true;
        }
        catch
        {
            return false; // Ignore bad env formats; validation will catch
        }
    }

    private static object? ConvertValue(Type type, string raw)
    {
        if (type == typeof(string)) return raw;
        if (type == typeof(int)) return int.Parse(raw);
        if (type == typeof(bool)) return bool.Parse(raw);
        if (type == typeof(string[])) return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (type == typeof(HashSet<string>)) return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        if (type == typeof(Microsoft.Extensions.Logging.LogLevel))
        {
            if (Enum.TryParse(typeof(Microsoft.Extensions.Logging.LogLevel), raw, true, out var enumValue))
            {
                return enumValue!;
            }
            throw new ArgumentException($"Invalid log level '{raw}'");
        }
        if (type == typeof(FeedMode))
        {
            if (Enum.TryParse(typeof(FeedMode), raw, true, out var fm))
            {
                return fm;
            }
            throw new ArgumentException($"Invalid feed mode '{raw}'");
        }
        if (type == typeof(RealFeedProvider))
        {
            if (Enum.TryParse(typeof(RealFeedProvider), raw, true, out var rfp))
                return rfp;
            throw new ArgumentException($"Invalid real feed provider '{raw}'");
        }
        if (type == typeof(PrimaryEnvironment) || Nullable.GetUnderlyingType(type) == typeof(PrimaryEnvironment))
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (Enum.TryParse(underlying, raw, true, out var penv))
                return penv;
            throw new ArgumentException($"Invalid primary environment '{raw}'");
        }
        if (type == typeof(ExcelWriterType))
        {
            if (Enum.TryParse(typeof(ExcelWriterType), raw, true, out var ewt))
            {
                return ewt;
            }
            throw new ArgumentException($"Invalid Excel writer type '{raw}'");
        }
        return Convert.ChangeType(raw, type);
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static AppConfiguration Merge(AppConfiguration baseline, AppConfiguration overlay)
    {
        // Simple property-wise overlay where overlay values replace baseline if different from defaults.
        foreach (var prop in typeof(AppConfiguration).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var value = prop.GetValue(overlay);
            if (value is null) continue; // skip nulls
            prop.SetValue(baseline, value);
        }
        return baseline;
    }
}
