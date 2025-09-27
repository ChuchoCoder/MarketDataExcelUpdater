@echo off
echo Starting Market Data Excel Updater Demo
echo This demo uses the LateBound Excel writer (version agnostic COM)
echo This works with any Excel installation without version conflicts
echo.

REM Copy the configuration file to the expected location
copy appsettings.example.json src\MarketDataExcelUpdater\appsettings.json

REM Run the application
dotnet run --project src\MarketDataExcelUpdater

pause