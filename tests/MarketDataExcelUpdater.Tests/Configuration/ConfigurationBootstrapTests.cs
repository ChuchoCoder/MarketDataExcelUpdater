using FluentAssertions;
using MarketDataExcelUpdater.Core.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace MarketDataExcelUpdater.Tests.Configuration;

public class ConfigurationBootstrapTests
{
    [Fact]
    public void LoadWithLogging_returns_valid_config_and_logs()
    {
        var logger = new Mock<ILogger>();
        var cfg = ConfigurationBootstrap.LoadWithLogging(logger.Object);
        cfg.ExcelFilePath.Should().NotBeNullOrEmpty();
        logger.Verify(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Configuration summary")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ), Times.Once);
    }

    [Fact]
    public void LoadWithLogging_propagates_validation_exception()
    {
        var logger = new Mock<ILogger>();
        // Force invalid config via env var
        Environment.SetEnvironmentVariable("ExcelFilePath", "bad.txt");
        try
        {
            Action act = () => ConfigurationBootstrap.LoadWithLogging(logger.Object);
            act.Should().Throw<ConfigurationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ExcelFilePath", null);
        }
    }
}