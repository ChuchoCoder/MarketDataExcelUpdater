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
        private readonly Dictionary<string, int> _columnMapping = new(); // worksheet_columnName -> columnIndex
        private readonly Dictionary<string, int> _nextColumnIndex = new(); // worksheetName -> next available column
        private string? _currentFilePath;
        private bool _disposed;

        // Buffering configuration and state
        private readonly TimeSpan _bufferFlushInterval;
        private readonly int _maxBufferSize;
        private readonly Dictionary<string, CellUpdate> _updateBuffer = new(); // Key: SheetName_ColumnName_RowIndex, Value: Latest update
        private readonly object _bufferLock = new object();
        private System.Threading.Timer? _flushTimer;

        // Exponential backoff state
        private DateTime? _lastFailureTime;
        private int _consecutiveFailures;
        private readonly TimeSpan _maxBackoffDelay = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _baseDelay = TimeSpan.FromMilliseconds(500);

        public ExcelLateBoundWriter(ILogger<ExcelLateBoundWriter> logger, 
            TimeSpan? bufferFlushInterval = null, 
            int maxBufferSize = 1000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _bufferFlushInterval = bufferFlushInterval ?? TimeSpan.FromMilliseconds(500);
            _maxBufferSize = maxBufferSize;
            
            // Initialize the flush timer
            _flushTimer = new System.Threading.Timer(FlushBufferCallback, null, _bufferFlushInterval, _bufferFlushInterval);
            
            _logger.LogInformation("ExcelLateBoundWriter initialized with buffer flush interval: {FlushInterval}ms, max buffer size: {MaxBufferSize}", 
                _bufferFlushInterval.TotalMilliseconds, _maxBufferSize);
        }

        public async ValueTask WriteAsync(UpdateBatch batch, CancellationToken ct = default)
        {
            if (_workbook == null)
                throw new InvalidOperationException("Excel workbook not initialized. Ensure a file path is set.");

            // Check if we're in a backoff period
            if (IsInBackoffPeriod())
            {
                var remainingBackoff = GetRemainingBackoffTime();
                _logger.LogDebug("Skipping Excel write - in backoff period for {RemainingSeconds:F1} more seconds", 
                    remainingBackoff.TotalSeconds);
                return; // Skip this write attempt, let market data continue flowing
            }

            _logger.LogDebug("Adding batch with {Count} updates to buffer", batch.Updates.Count);

            int duplicatesReplaced = 0;
            
            // Add updates to buffer with deduplication instead of writing immediately
            lock (_bufferLock)
            {
                foreach (var update in batch.Updates)
                {
                    string cellKey = GetCellKey(update);
                    
                    // Track if we're replacing a duplicate
                    if (_updateBuffer.ContainsKey(cellKey))
                    {
                        duplicatesReplaced++;
                    }
                    
                    // Last update wins - this automatically deduplicates
                    _updateBuffer[cellKey] = update;
                }
                
                // Log deduplication statistics
                if (duplicatesReplaced > 0)
                {
                    _logger.LogDebug("Deduplicated {DuplicateCount} updates from batch of {BatchSize}. Buffer now has {BufferSize} unique cells", 
                        duplicatesReplaced, batch.Updates.Count, _updateBuffer.Count);
                }
                
                // If buffer exceeds max size, trigger immediate flush
                if (_updateBuffer.Count >= _maxBufferSize)
                {
                    _logger.LogInformation("Buffer size ({BufferSize}) exceeded max size ({MaxSize}), triggering immediate flush", 
                        _updateBuffer.Count, _maxBufferSize);
                    
                    // Process immediately without waiting for timer
                    _ = Task.Run(async () => await FlushBufferAsync(ct));
                }
            }

            await Task.CompletedTask;
        }

        public async ValueTask FlushAsync(CancellationToken ct = default)
        {
            // First, flush any buffered updates
            await FlushBufferAsync(ct);

            // Skip flush if we're in backoff period to avoid additional COM errors
            if (IsInBackoffPeriod())
            {
                var remainingBackoff = GetRemainingBackoffTime();
                _logger.LogDebug("Skipping Excel flush - in backoff period for {RemainingSeconds:F1} more seconds", 
                    remainingBackoff.TotalSeconds);
                return;
            }

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
                _logger.LogWarning(ex, "Failed to flush Excel workbook during backoff period");
                HandleWriteFailure(ex);
                // Don't rethrow during normal operations - allow graceful degradation
            }
        }

        /// <summary>
        /// Initialize Excel and open/create the workbook
        /// </summary>
        public async Task CreateOrOpenWorkbookAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Initializing Excel with late binding for live updates to {FilePath}", filePath);
                
                // Resolve path relative to application base directory (where the exe runs from)
                // This ensures relative paths work correctly regardless of current working directory
                if (Path.IsPathRooted(filePath))
                {
                    _currentFilePath = Path.GetFullPath(filePath);
                }
                else
                {
                    var basePath = AppContext.BaseDirectory;
                    _currentFilePath = Path.GetFullPath(Path.Combine(basePath, filePath));
                }

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
                
                // _logger.LogDebug("Updated cell [{Row}, {Col}] in sheet '{Sheet}' with value: {Value}", 
                //     update.RowIndex, colIndex, update.SheetName, update.Value);
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
                string worksheetName = (string)worksheet.Name;
                string mappingKey = $"{worksheetName}_{columnName}";
                
                // Check if we already have this column mapped
                if (_columnMapping.TryGetValue(mappingKey, out int existingColumn))
                {
                    // _logger.LogDebug("Found cached column '{ColumnName}' at index {ColumnIndex} in sheet '{SheetName}'", 
                    //     columnName, existingColumn, worksheetName);
                    return existingColumn;
                }

                // Initialize next column index for this worksheet if not set
                if (!_nextColumnIndex.TryGetValue(worksheetName, out int nextColumn))
                {
                    // For new worksheets, scan existing columns first
                    nextColumn = 1;
                    try
                    {
                        dynamic usedRange = worksheet.UsedRange;
                        if (usedRange != null)
                        {
                            int lastColumn = usedRange.Columns.Count;
                            
                            // Scan existing headers to build initial mapping
                            for (int col = 1; col <= lastColumn; col++)
                            {
                                try
                                {
                                    dynamic cellValue = worksheet.Cells[1, col];
                                    string headerValue = cellValue?.Value?.ToString() ?? "";
                                    
                                    if (!string.IsNullOrEmpty(headerValue))
                                    {
                                        string existingMappingKey = $"{worksheetName}_{headerValue}";
                                        _columnMapping[existingMappingKey] = col;
                                        _logger.LogDebug("Found existing header '{HeaderValue}' at column {Column} in sheet '{SheetName}'", 
                                            headerValue, col, worksheetName);
                                    }
                                }
                                catch
                                {
                                    // Skip if unable to read cell
                                }
                            }
                            nextColumn = lastColumn + 1;
                        }
                    }
                    catch
                    {
                        // If we can't read the used range, start from column 1
                        nextColumn = 1;
                    }
                    
                    _nextColumnIndex[worksheetName] = nextColumn;
                }
                else
                {
                    nextColumn = _nextColumnIndex[worksheetName];
                }

                // Check if the column already exists after scanning
                if (_columnMapping.TryGetValue(mappingKey, out int foundColumn))
                {
                    _logger.LogDebug("Found existing column '{ColumnName}' at index {ColumnIndex} in sheet '{SheetName}' during scan", 
                        columnName, foundColumn, worksheetName);
                    return foundColumn;
                }

                // Create new column
                worksheet.Cells[1, nextColumn] = columnName;
                
                // Format the header
                dynamic newHeaderCell = worksheet.Cells[1, nextColumn];
                newHeaderCell.Font.Bold = true;
                
                // Cache the mapping and update next column index
                _columnMapping[mappingKey] = nextColumn;
                _nextColumnIndex[worksheetName] = nextColumn + 1;
                
                _logger.LogInformation("Created new column '{ColumnName}' at index {ColumnIndex} in sheet '{SheetName}'", 
                    columnName, nextColumn, worksheetName);
                
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

        #region Buffer Management

        /// <summary>
        /// Generate a unique key for a cell update to enable deduplication
        /// </summary>
        private static string GetCellKey(CellUpdate update)
        {
            return $"{update.SheetName}_{update.ColumnName}_{update.RowIndex}";
        }

        /// <summary>
        /// Timer callback method for periodic buffer flushing
        /// </summary>
        private void FlushBufferCallback(object? state)
        {
            // Use async void for fire-and-forget timer callback
            _ = Task.Run(async () => await FlushBufferAsync(CancellationToken.None));
        }

        /// <summary>
        /// Flush the buffered updates to Excel
        /// </summary>
        private async Task FlushBufferAsync(CancellationToken ct = default)
        {
            List<CellUpdate> updatesToProcess;
            
            // Extract updates from buffer under lock
            lock (_bufferLock)
            {
                if (_updateBuffer.Count == 0)
                    return; // Nothing to flush
                
                // Convert dictionary values to list for processing
                updatesToProcess = new List<CellUpdate>(_updateBuffer.Values);
                _updateBuffer.Clear();
            }

            // Check if we're in a backoff period before processing
            if (IsInBackoffPeriod())
            {
                var remainingBackoff = GetRemainingBackoffTime();
                _logger.LogDebug("Skipping buffered Excel write - in backoff period for {RemainingSeconds:F1} more seconds. " +
                    "Discarding {UpdateCount} updates", remainingBackoff.TotalSeconds, updatesToProcess.Count);
                return;
            }

            _logger.LogInformation("=== EXCEL LATE-BOUND BUFFER FLUSH START ===");
            _logger.LogInformation("Flushing {Count} deduplicated updates to Excel", updatesToProcess.Count);

            try
            {
                foreach (var update in updatesToProcess)
                {
                    await WriteUpdateAsync(update, ct);
                }

                // Reset failure tracking on successful write
                ResetBackoffState();
                _logger.LogInformation("=== EXCEL LATE-BOUND BUFFER FLUSH COMPLETE ===");
            }
            catch (Exception ex)
            {
                HandleWriteFailure(ex);
                // Don't rethrow - allow timer to continue operating
            }
        }

        #endregion

        #region Exponential Backoff Logic

        /// <summary>
        /// Check if we're currently in a backoff period due to previous failures
        /// </summary>
        private bool IsInBackoffPeriod()
        {
            if (_lastFailureTime == null || _consecutiveFailures == 0)
                return false;

            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime.Value;
            var requiredBackoffTime = CalculateBackoffDelay();
            
            return timeSinceLastFailure < requiredBackoffTime;
        }

        /// <summary>
        /// Get the remaining time in the current backoff period
        /// </summary>
        private TimeSpan GetRemainingBackoffTime()
        {
            if (_lastFailureTime == null)
                return TimeSpan.Zero;

            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime.Value;
            var requiredBackoffTime = CalculateBackoffDelay();
            var remaining = requiredBackoffTime - timeSinceLastFailure;
            
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Calculate the current backoff delay based on consecutive failures
        /// </summary>
        private TimeSpan CalculateBackoffDelay()
        {
            if (_consecutiveFailures <= 0)
                return TimeSpan.Zero;

            // Exponential backoff: base * 2^(failures-1), capped at maxBackoffDelay
            var delay = TimeSpan.FromMilliseconds(
                _baseDelay.TotalMilliseconds * Math.Pow(2, _consecutiveFailures - 1));
            
            return delay > _maxBackoffDelay ? _maxBackoffDelay : delay;
        }

        /// <summary>
        /// Handle a write failure by updating backoff state and logging appropriately
        /// </summary>
        private void HandleWriteFailure(Exception ex)
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            
            var nextBackoffDelay = CalculateBackoffDelay();
            
            // Log at different levels based on failure count to reduce verbosity
            if (_consecutiveFailures == 1)
            {
                _logger.LogWarning(ex, "Excel write failed. Market data continues flowing. Next attempt in {DelaySeconds:F1}s", 
                    nextBackoffDelay.TotalSeconds);
            }
            else if (_consecutiveFailures <= 3)
            {
                _logger.LogInformation("Excel write failed (attempt {FailureCount}). Next attempt in {DelaySeconds:F1}s", 
                    _consecutiveFailures, nextBackoffDelay.TotalSeconds);
            }
            else
            {
                // After 3 failures, only log every 5th failure to reduce noise
                if (_consecutiveFailures % 5 == 0)
                {
                    _logger.LogWarning("Excel write continues to fail ({FailureCount} consecutive failures). " +
                        "Next attempt in {DelaySeconds:F1}s. Check Excel application state.", 
                        _consecutiveFailures, nextBackoffDelay.TotalSeconds);
                }
                else
                {
                    _logger.LogDebug("Excel write failed (attempt {FailureCount}). Next attempt in {DelaySeconds:F1}s", 
                        _consecutiveFailures, nextBackoffDelay.TotalSeconds);
                }
            }
        }

        /// <summary>
        /// Reset backoff state after successful write
        /// </summary>
        private void ResetBackoffState()
        {
            if (_consecutiveFailures > 0)
            {
                _logger.LogInformation("Excel write recovered after {FailureCount} consecutive failures", 
                    _consecutiveFailures);
                _consecutiveFailures = 0;
                _lastFailureTime = null;
            }
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            try
            {
                // Stop the timer first to prevent new flushes
                if (_flushTimer != null)
                {
                    _flushTimer.Dispose();
                    _flushTimer = null;
                }

                // Flush any remaining buffered updates before closing
                await FlushBufferAsync();

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
        }

        // Implement IDisposable for legacy compatibility
        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }
    }
}