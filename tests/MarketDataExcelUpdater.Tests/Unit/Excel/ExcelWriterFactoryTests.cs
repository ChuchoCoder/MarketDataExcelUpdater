using FluentAssertions;
using MarketDataExcelUpdater.Core.Configuration;
using MarketDataExcelUpdater.Infrastructure.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketDataExcelUpdater.Tests.Unit.Excel;

public sealed class ExcelWriterFactoryTests
{
    private readonly ILoggerFactory _loggerFactory = new NullLoggerFactory();

    [Fact]
    public void CreateWriter_WithLateBoundType_ReturnsExcelLateBoundWriter()
    {
        // Act
        var writer = ExcelWriterFactory.CreateWriter(ExcelWriterType.LateBound, _loggerFactory);

        // Assert
        writer.Should().BeOfType<ExcelLateBoundWriter>();
    }

    [Fact]
    public void CreateWriter_WithInvalidType_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        var action = () => ExcelWriterFactory.CreateWriter((ExcelWriterType)999, _loggerFactory);
        action.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("writerType");
    }

    [Theory]
    [InlineData(ExcelWriterType.LateBound, "Late Bound COM (live updates, version agnostic)")]
    public void GetWriterDescription_ReturnsExpectedDescription(ExcelWriterType writerType, string expectedDescription)
    {
        // Act
        var description = ExcelWriterFactory.GetWriterDescription(writerType);

        // Assert
        description.Should().Be(expectedDescription);
    }

    [Theory]
    [InlineData(ExcelWriterType.LateBound, false)]
    public void RequiresClosedExcel_ReturnsExpectedValue(ExcelWriterType writerType, bool expectedRequiresClosed)
    {
        // Act
        var requiresClosed = ExcelWriterFactory.RequiresClosedExcel(writerType);

        // Assert
        requiresClosed.Should().Be(expectedRequiresClosed);
    }

    [Fact]
    public void GetWriterDescription_WithInvalidType_ReturnsUnknown()
    {
        // Act
        var description = ExcelWriterFactory.GetWriterDescription((ExcelWriterType)999);

        // Assert
        description.Should().Be("Unknown");
    }

    [Fact]
    public void RequiresClosedExcel_WithInvalidType_ReturnsTrue()
    {
        // Act
        var requiresClosed = ExcelWriterFactory.RequiresClosedExcel((ExcelWriterType)999);

        // Assert
        requiresClosed.Should().BeTrue(); // Default to safer assumption
    }
}