using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MarketDataExcelUpdater.Core.Abstractions;
using MarketDataExcelUpdater.Core;

namespace MarketDataExcelUpdater.Infrastructure.Excel
{
    /// <summary>
    /// Excel writer implementation using late-bound COM interop.
    /// This avoids Office Interop assembly version dependencies by using reflection.
    /// Provides live Excel updates without assembly version conflicts.
    /// </summary>
    public class ExcelLateBoundWriter : IExcelWriter
    {
        private readonly ILogger<ExcelLateBoundWriter> _logger;
        private dynamic? _excelApp;
        private dynamic? _workbook;
        private readonly Dictionary<string, dynamic> _worksheets = new();
        private string? _currentFilePath;
        private bool _disposed;

        public ExcelLateBoundWriter(ILogger<ExcelLateBoundWriter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async ValueTask WriteAsync(UpdateBatch batch, CancellationToken ct = default)
        {
            if (_workbook == null)
                throw new InvalidOperationException("Excel workbook not initialized. Ensure a file path is set.");

            _logger.LogInformation("=== EXCEL LATE-BOUND WRITE BATCH START ===");
            _logger.LogInformation("Writing batch with {Count} updates", batch.Updates.Count);

            try
            {
                foreach (var update in batch.Updates)
                {
                    await WriteUpdateAsync(update, ct);
                }

                _logger.LogInformation("=== EXCEL LATE-BOUND WRITE BATCH COMPLETE ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write batch to Excel");
                throw;
            }
        }

        public async ValueTask FlushAsync(CancellationToken ct = default)
        {
            try
            {
                if (_workbook != null)
                {
                    _logger.LogInformation("=== EXCEL LATE-BOUND FLUSH START ===");
                    _workbook.Save();
                    _logger.LogInformation("=== EXCEL LATE-BOUND FLUSH COMPLETE ===");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush Excel workbook");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Initialize Excel and open/create the workbook
        /// </summary>
        public async Task CreateOrOpenWorkbookAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Initializing Excel with late binding for live updates to {FilePath}", filePath);
                _currentFilePath = Path.GetFullPath(filePath);

                // Create Excel application using late binding (COM)
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                    throw new InvalidOperationException("Excel application not found. Please ensure Microsoft Excel is installed.");

                _excelApp = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException("Failed to create Excel application instance.");

                _excelApp.Visible = true; // Make Excel visible for live updates
                _excelApp.DisplayAlerts = false; // Suppress Excel alerts

                _logger.LogInformation("Excel application started successfully");

                // Check if the file exists, if not create a new workbook
                if (File.Exists(_currentFilePath))
                {
                    _logger.LogInformation("Opening existing Excel file: {FilePath}", _currentFilePath);
                    _workbook = _excelApp.Workbooks.Open(_currentFilePath);
                }
                else
                {
                    _logger.LogInformation("Creating new Excel workbook: {FilePath}", _currentFilePath);
                    
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(_currentFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    _workbook = _excelApp.Workbooks.Add();
                    _workbook.SaveAs(_currentFilePath);
                }

                _logger.LogInformation("Excel workbook initialized successfully");
                await Task.CompletedTask;
            }
            catch (COMException ex)
            {
                _logger.LogError(ex, "COM error while initializing Excel: {Message} (HRESULT: 0x{HResult:X8})", ex.Message, ex.HResult);
                throw new InvalidOperationException($"Failed to initialize Excel COM interface: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while initializing Excel: {Message}", ex.Message);
                throw;
            }
        }

        private async Task WriteUpdateAsync(CellUpdate update, CancellationToken ct)
        {
            try
            {
                // Get or create the worksheet
                dynamic worksheet = await GetOrCreateWorksheetAsync(update.SheetName, ct);

                // Get or create column index for this column name
                int colIndex = await GetOrCreateColumnAsync(worksheet, update.ColumnName, ct);

                // Use late binding to update the cell
                worksheet.Cells[update.RowIndex, colIndex] = update.Value;
                
                _logger.LogDebug("Updated cell [{Row}, {Col}] in sheet '{Sheet}' with value: {Value}", 
                    update.RowIndex, colIndex, update.SheetName, update.Value);
            }
            catch (COMException ex)
            {
                _logger.LogError(ex, "COM error while updating cell [{Row}, {Col}] in sheet '{Sheet}': {Message}", 
                    update.RowIndex, "?", update.SheetName, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cell [{Row}, {Col}] in sheet '{Sheet}': {Message}", 
                    update.RowIndex, "?", update.SheetName, ex.Message);
                throw;
            }
        }

        private async Task<dynamic> GetOrCreateWorksheetAsync(string sheetName, CancellationToken ct)
        {
            if (_worksheets.TryGetValue(sheetName, out var cachedSheet))
                return cachedSheet;

            try
            {
                dynamic worksheet;
                
                // Try to get existing worksheet
                try
                {
                    worksheet = _workbook.Worksheets[sheetName];
                    _logger.LogDebug("Found existing worksheet: {SheetName}", sheetName);
                }
                catch
                {
                    // Worksheet doesn't exist, create it
                    _logger.LogInformation("Creating new worksheet: {SheetName}", sheetName);
                    worksheet = _workbook.Worksheets.Add();
                    worksheet.Name = sheetName;
                }

                _worksheets[sheetName] = worksheet;
                await Task.CompletedTask;
                return worksheet;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create worksheet '{SheetName}'", sheetName);
                throw;
            }
        }

        private async Task<int> GetOrCreateColumnAsync(dynamic worksheet, string columnName, CancellationToken ct)
        {
            try
            {
                // Check if we have cached column mappings for this worksheet
                string worksheetKey = $"{worksheet.Name}_{columnName}";
                
                // Check if header row exists and find the column
                dynamic headerRow = worksheet.Rows[1];
                
                // Get the used range to find the last column with data
                dynamic usedRange = worksheet.UsedRange;
                int lastColumn = usedRange?.Columns?.Count ?? 0;
                
                // Search for existing column with this header
                for (int col = 1; col <= lastColumn; col++)
                {
                    try
                    {
                        dynamic cellValue = worksheet.Cells[1, col];
                        string headerValue = cellValue?.Value?.ToString() ?? "";
                        
                        if (string.Equals(headerValue, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Found existing column '{ColumnName}' at index {ColumnIndex} in sheet '{SheetName}'", 
                                columnName, col, (string)worksheet.Name);
                            return col;
                        }
                    }
                    catch
                    {
                        // Skip if unable to read cell
                        continue;
                    }
                }

                // Create new column - find the next available column
                int nextColumn = lastColumn + 1;
                worksheet.Cells[1, nextColumn] = columnName;
                
                // Format the header
                dynamic newHeaderCell = worksheet.Cells[1, nextColumn];
                newHeaderCell.Font.Bold = true;
                
                _logger.LogInformation("Created new column '{ColumnName}' at index {ColumnIndex} in sheet '{SheetName}'", 
                    columnName, nextColumn, (string)worksheet.Name);
                
                await Task.CompletedTask;
                return nextColumn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create column '{ColumnName}': {Message}", columnName, ex.Message);
                throw;
            }
        }

        private static int GetColumnIndex(string columnName)
        {
            // Legacy method - kept for compatibility but should not be used
            // If it's already a number, return it
            if (int.TryParse(columnName, out int colIndex))
                return colIndex;

            // Convert column name (A, B, C, ..., AA, AB, etc.) to 1-based index
            // This is problematic for descriptive column names and should not be used
            int result = 0;
            for (int i = 0; i < columnName.Length; i++)
            {
                char c = char.ToUpper(columnName[i]);
                if (c >= 'A' && c <= 'Z')
                {
                    result = result * 26 + (c - 'A' + 1);
                }
                else
                {
                    // Non-letter character - this method won't work for descriptive names
                    return -1;
                }
            }
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            try
            {
                if (_workbook != null)
                {
                    _logger.LogInformation("Closing Excel workbook");
                    _workbook.Close(SaveChanges: true);
                    Marshal.ReleaseComObject(_workbook);
                    _workbook = null;
                }

                if (_excelApp != null)
                {
                    _logger.LogInformation("Closing Excel application");
                    _excelApp.Quit();
                    Marshal.ReleaseComObject(_excelApp);
                    _excelApp = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing Excel resources: {Message}", ex.Message);
            }

            _disposed = true;
            await Task.CompletedTask;
        }

        // Implement IDisposable for legacy compatibility
        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }
    }
}