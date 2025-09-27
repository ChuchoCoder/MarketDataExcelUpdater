# Live Excel Updates Demo

This script demonstrates how to test the LateBound Excel writer for real-time updates.

## Prerequisites

1. Windows operating system
2. Microsoft Excel installed
3. .NET 9 runtime

## Demo Steps

### 1. Create Live Configuration

Copy the `appsettings.example.json` to `appsettings.json`:

```bash
copy appsettings.example.json appsettings.json
```

### 2. Update Configuration

Edit `appsettings.json` to use the LateBound writer:

```json
{
  "ExcelFilePath": "LiveDemo.xlsx",
  "ExcelWriterType": "LateBound",
  "FeedMode": "Demo",
  "BatchMaxAgeMs": 100,
  "LogLevel": "Information"
}
```

### 3. Test Scenario A: Excel Closed

1. Ensure Excel is **closed**
2. Run the application:
   ```bash
   dotnet run --project src\MarketDataExcelUpdater
   ```
3. The application will create `LiveDemo.xlsx` and start updating it
4. **Open Excel** while the application is running
5. Navigate to `LiveDemo.xlsx` → `MarketData` sheet
6. **Watch the data update in real-time!**

### 4. Test Scenario B: Excel Already Open

1. Open Excel first
2. Create a new workbook and save it as `LiveDemo.xlsx`
3. Run the application:
   ```bash  
   dotnet run --project src\MarketDataExcelUpdater
   ```
4. The application will connect to the existing workbook
5. **Watch the data populate and update live in the open Excel sheet**

### 5. Test Scenario C: Add Formulas

While the application is running and updating data:

1. In Excel, go to an empty column (e.g., column M)
2. Add a formula like `=IF(L2>0, "UP", "DOWN")` (assuming column L has price data)
3. Copy the formula down to other rows
4. **Watch your formulas recalculate automatically as new data arrives!**

## Expected Behavior

- Data updates appear immediately in open Excel workbooks
- Formulas recalculate automatically with new data
- Charts and pivot tables refresh in real-time
- No file locking issues - Excel remains fully interactive
- Performance is smooth with minimal Excel UI freezing

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Excel not found" error | Ensure Excel is installed and accessible via COM |
| No data appearing | Check if workbook path matches ExcelFilePath setting |
| Performance issues | Reduce BatchMaxAgeMs or increase BatchHighWatermark |
| Excel becomes unresponsive | Lower the update frequency in configuration |

## LateBound Writer Benefits

The LateBound Excel writer provides several advantages:

- **Version Agnostic**: Works with any Excel installation without version conflicts
- **Real-time Updates**: Excel can remain open and will show live data updates
- **No Assembly Dependencies**: Uses COM reflection to avoid version-specific references
- **xlwings-like Experience**: Similar functionality to Python's xlwings library

The LateBound writer provides the xlwings-like experience you requested!