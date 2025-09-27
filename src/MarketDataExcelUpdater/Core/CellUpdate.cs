namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Represents a single pending cell write operation.
/// </summary>
public sealed class CellUpdate
{
    public string SheetName { get; init; } = string.Empty;
    public string ColumnName { get; init; } = string.Empty;
    public int RowIndex { get; init; }
    public object? Value { get; init; }

    // Backward compatibility properties
    public int Row => RowIndex;
    public int Column => throw new NotSupportedException("Use ColumnName for string-based column identification");

    public CellUpdate()
    {
    }

    public CellUpdate(string sheetName, string columnName, int rowIndex, object? value)
    {
        SheetName = sheetName;
        ColumnName = columnName;
        RowIndex = rowIndex;
        Value = value;
    }
}
