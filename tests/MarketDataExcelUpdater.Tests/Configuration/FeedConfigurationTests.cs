using FluentAssertions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Core.Abstractions;

namespace MarketDataExcelUpdater.Tests.Configuration;

public class FeedConfigurationTests
{
    [Fact]
    public void Default_feed_mode_is_demo()
    {
        var cfg = ConfigurationLoader.Load(jsonPath: Guid.NewGuid()+".json", optional: true);
        cfg.FeedMode.Should().Be(FeedMode.Demo);
    }

    [Fact]
    public void Real_mode_requires_provider_specification()
    {
        var file = "temp-real.json";
        try
        {
            File.WriteAllText(file, "{ \"ExcelFilePath\": \"MarketData.xlsx\", \"FeedMode\": \"Real\" }");
            Action act = () => ConfigurationLoader.Load(jsonPath: file, optional: true);
            var ex = act.Should().Throw<ConfigurationException>().Which;
            ex.Errors.Should().Contain(e => e.Contains("RealFeedProvider must be specified"));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void Real_mode_with_primary_credentials_passes_validation()
    {
        var file = "temp-real-ok.json";
        try
        {
            File.WriteAllText(file, "{ \"ExcelFilePath\": \"MarketData.xlsx\", \"FeedMode\": \"Real\", \"RealFeedProvider\": \"Primary\", \"PrimaryUsername\": \"user\", \"PrimaryPassword\": \"pass\", \"PrimaryEnvironment\": \"Test\" }");
            var cfg = ConfigurationLoader.Load(jsonPath: file, optional: true);
            cfg.FeedMode.Should().Be(FeedMode.Real);
            cfg.RealFeedProvider.Should().Be(RealFeedProvider.Primary);
            cfg.PrimaryUsername.Should().Be("user");
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
