using FluentAssertions;
using MarketDataExcelUpdater.Core.Configuration;

namespace MarketDataExcelUpdater.Tests.Configuration;

public class ConfigurationLoaderTests
{
    [Fact]
    public void Load_defaults_success()
    {
        var cfg = ConfigurationLoader.Load(jsonPath: Guid.NewGuid()+".json", optional: true);
        cfg.ExcelFilePath.Should().Be("MarketData.xlsx");
        cfg.StaleSeconds.Should().Be(5);
    }

    [Fact]
    public void Load_invalid_file_throws_when_not_optional()
    {
        Action act = () => ConfigurationLoader.Load(jsonPath: "does-not-exist.json", optional: false);
        act.Should().Throw<ConfigurationException>();
    }

    [Fact]
    public void Env_overrides_apply()
    {
        const string fileName = "temp-appsettings.json";
        try
        {
            // Minimal JSON
            File.WriteAllText(fileName, "{ \"ExcelFilePath\": \"MarketData.xlsx\" }");

            Environment.SetEnvironmentVariable("StaleSeconds", "12");
            Environment.SetEnvironmentVariable("BatchHighWatermark", "250");

            var cfg = ConfigurationLoader.Load(jsonPath: fileName, optional: true);
            cfg.StaleSeconds.Should().Be(12);
            cfg.BatchHighWatermark.Should().Be(250);
        }
        finally
        {
            Environment.SetEnvironmentVariable("StaleSeconds", null);
            Environment.SetEnvironmentVariable("BatchHighWatermark", null);
            if (File.Exists(fileName)) File.Delete(fileName);
        }
    }

    [Fact]
    public void Validation_errors_are_aggregated()
    {
        const string fileName = "bad-config.json";
        try
        {
            // Intentionally invalid: BatchMaxAgeMs >= StaleSeconds*1000 AND wrong extension
            File.WriteAllText(fileName, "{ \"ExcelFilePath\": \"data.txt\", \"StaleSeconds\": 5, \"BatchMaxAgeMs\": 6000 }" );
            Action act = () => ConfigurationLoader.Load(jsonPath: fileName, optional: true);
            var ex = act.Should().Throw<ConfigurationException>().Which;
            ex.Errors.Should().Contain(e => e.Contains(".xlsx"));
            ex.Errors.Should().Contain(e => e.Contains("BatchMaxAgeMs"));
        }
        finally
        {
            if (File.Exists(fileName)) File.Delete(fileName);
        }
    }

    [Fact]
    public void Env_override_log_level_applies()
    {
        const string fileName = "temp-appsettings.json";
        try
        {
            File.WriteAllText(fileName, "{ \"ExcelFilePath\": \"MarketData.xlsx\" }");
            Environment.SetEnvironmentVariable("LogLevel", "Debug");
            var cfg = ConfigurationLoader.Load(jsonPath: fileName, optional: true);
            ((int)cfg.LogLevel).Should().Be((int)Microsoft.Extensions.Logging.LogLevel.Debug);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LogLevel", null);
            if (File.Exists(fileName)) File.Delete(fileName);
        }
    }

    [Fact]
    public void Invalid_log_level_env_causes_configuration_error()
    {
        const string fileName = "temp-appsettings.json";
        try
        {
            File.WriteAllText(fileName, "{ \"ExcelFilePath\": \"MarketData.xlsx\" }");
            Environment.SetEnvironmentVariable("LogLevel", "NotARealLevel");
            // With .NET's configuration binder, invalid enum values will cause binding to throw a configuration exception
            Action act = () => ConfigurationLoader.Load(jsonPath: fileName, optional: true);
            act.Should().Throw<ConfigurationException>()
               .Which.Errors.Should().Contain(e => e.Contains("Failed to convert configuration value", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LogLevel", null);
            if (File.Exists(fileName)) File.Delete(fileName);
        }
    }
}