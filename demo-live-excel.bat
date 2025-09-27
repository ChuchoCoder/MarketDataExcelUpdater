@echo off
echo ========================================
echo Live Excel Updates Demo with .NET
echo ========================================
echo.
echo This demo shows how the LateBound Excel writer
echo enables real-time updates to open Excel workbooks,
echo similar to xlwings in Python.
echo.

REM Create temporary configuration file for the demo
echo { > appsettings.json
echo   "ExcelFilePath": "LiveDemo.xlsx", >> appsettings.json
echo   "ExcelWriterType": "LateBound", >> appsettings.json
echo   "FeedMode": "Demo", >> appsettings.json
echo   "BatchMaxAgeMs": 200, >> appsettings.json
echo   "LogLevel": "Information" >> appsettings.json
echo } >> appsettings.json

echo Configuration created: appsettings.json
type appsettings.json
echo.
echo NOTE: This demo uses the LateBound Excel writer for live updates.
echo Ensure Microsoft Excel is installed on your system.
echo.

echo Instructions:
echo 1. This demo will start updating LiveDemo.xlsx
echo 2. While it's running, open Excel and navigate to LiveDemo.xlsx
echo 3. Watch the MarketData sheet update in real-time!
echo 4. Try adding formulas - they'll recalculate automatically
echo 5. Press Ctrl+C to stop the demo
echo.

pause

echo Starting live Excel updates demo...
echo.
dotnet run --project src\MarketDataExcelUpdater

REM Clean up configuration file
del appsettings.json 2>nul

echo.
echo Demo completed. Configuration file cleaned up.
pause