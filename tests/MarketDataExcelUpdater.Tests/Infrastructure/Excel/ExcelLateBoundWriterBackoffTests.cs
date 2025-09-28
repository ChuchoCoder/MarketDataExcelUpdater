using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MarketDataExcelUpdater.Infrastructure.Excel;
using MarketDataExcelUpdater.Core;

namespace MarketDataExcelUpdater.Tests.Infrastructure.Excel;

public class ExcelLateBoundWriterBackoffTests
{
    [Fact]
    public async Task WriteAsync_WithoutWorkbook_ThrowsInvalidOperationException()
    {
        // Arrange
        var logger = new Mock<ILogger<ExcelLateBoundWriter>>();
        var writer = new ExcelLateBoundWriter(logger.Object);
        var batch = new UpdateBatch();

        // Act & Assert
        await writer.Invoking(w => w.WriteAsync(batch).AsTask())
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Excel workbook not initialized*");
    }

    [Fact]
    public async Task FlushAsync_WithoutWorkbook_DoesNotThrow()
    {
        // Arrange
        var logger = new Mock<ILogger<ExcelLateBoundWriter>>();
        var writer = new ExcelLateBoundWriter(logger.Object);

        // Act & Assert - should not throw even without workbook
        await writer.Invoking(w => w.FlushAsync().AsTask())
            .Should().NotThrowAsync();
    }

    [Fact]
    public void ExcelLateBoundWriter_CanBeInstantiated()
    {
        // Arrange
        var logger = new Mock<ILogger<ExcelLateBoundWriter>>();

        // Act
        var writer = new ExcelLateBoundWriter(logger.Object);

        // Assert
        writer.Should().NotBeNull();
    }
}