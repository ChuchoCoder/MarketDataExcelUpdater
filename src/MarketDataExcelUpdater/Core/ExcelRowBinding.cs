namespace MarketDataExcelUpdater.Core;

/// <summary>
/// Binds a symbol to its Excel row and column indices for required fields.
/// </summary>
public sealed class ExcelRowBinding
{
    public required string Symbol { get; init; }
    public required int RowIndex { get; init; }
    public required IReadOnlyDictionary<string, int> ColumnMap { get; init; } = 
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
