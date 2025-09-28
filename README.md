# MarketDataExcelUpdater

A lightweight .NET 9 console application that ingests (simulated) Argentina market data quotes and writes structured updates into an Excel workbook using late-bound COM Excel integration. Designed with simplicity, testability, and incremental extensibility in mind.

## Key Features

- Event-driven quote → model → Excel pipeline
- Batching with policy-based flush orchestration
- Stale monitoring & sequence tracking foundations
- Automatic worksheet/column creation (idempotent)
- Layered configuration (defaults → JSON → environment variables `MDX_*`)
- Synthetic demo feed (can be disabled) for immediate usability
- Pluggable real-feed architecture (Primary provider implemented via reflection)
- Graceful shutdown with final flush
- 60+ unit tests covering core logic & configuration

## Quick Start

```bash
# Build & test
dotnet build
dotnet test

# Run (creates MarketData.xlsx in working directory)
dotnet run --project src/MarketDataExcelUpdater
```

Press Ctrl+C to stop. A demo feed will continuously update the workbook until cancellation.

## Workbook Layout (Emergent)

Columns are created on demand. Two sheets will typically appear:

- `MarketData` (per-symbol rows starting at row 2; row 1 headers auto-generated)
- `Metrics` (heartbeat/metrics row at row 2)

Sample headers (not exhaustive): `Symbol`, `LastUpdate`, `IsStale`, `GapCount`, `Sequence`, `Last`, `Bid`, `Ask`, `BidSize`, `AskSize`, `Volume`, `Change`, `Open`, `High`, `Low`, `Timestamp`, `TotalQuotes`, `TotalGaps`, `StaleCount`, `InstrumentCount`.

Retention-related metrics (if retention enabled): `RetentionTotalEvicted`, `RetentionLastEvictionUtc`, `RetentionLastBatchEvicted`.

## Excel Writer

The application uses a late-bound COM Excel writer that provides real-time updates to open Excel workbooks without assembly version dependencies.

### LateBound Writer

- **Type**: `LateBound`
- **Behavior**: Live updates directly to open Excel applications via COM without assembly dependencies
- **Requirements**: Excel can remain **open** during updates (similar to xlwings in Python)
- **Prerequisites**: Microsoft Excel must be installed on the system
- **Use Case**: Real-time monitoring, interactive dashboards, live trading screens
- **Advantages**: Version-agnostic, works with any Excel installation, real-time formula updates, no COM version conflicts
- **Platform**: Windows only (requires Microsoft Excel to be installed)

### Excel Writer Configuration

The application is configured to use the LateBound writer by default:

```json
{
  "ExcelWriterType": "LateBound",
  "ExcelFilePath": "MarketData_Live.xlsx"
}
```

Or via environment variable:

```bash
set ExcelWriterType=LateBound
```

### Usage Benefits

The LateBound writer provides several advantages:

- **Version Agnostic**: Works with any Excel installation without assembly version conflicts
- **Real-time Updates**: Live updates visible while Excel is open
- **Interactive Analysis**: Formulas and charts update in real-time
- **No Assembly Dependencies**: Uses late-bound COM to avoid version-specific references
- **Reliable Live Integration**: Similar to xlwings functionality for Python

### Error Handling & Resilience

The Excel writer includes robust error handling for COM-related failures:

- **Exponential Backoff**: When Excel write operations fail (e.g., COM exception 0x800A01A8), the writer implements exponential backoff without retries
- **Graceful Degradation**: Market data continues flowing even when Excel writes fail - no data loss in the pipeline
- **Smart Logging**: Error verbosity decreases over time (detailed logs for first few failures, then reduced frequency to avoid log spam)
- **Automatic Recovery**: When Excel becomes available again, normal operations resume immediately
- **Backoff Duration**: Starts at 500ms, doubles with each consecutive failure, capped at 30 seconds maximum delay
- **Non-Blocking**: Failed Excel writes don't block or slow down market data ingestion

## Configuration

The application uses .NET's standard configuration system with the following priority order (highest to lowest):

1. Command-line arguments (if provided)
2. Environment variables
3. `appsettings.json` file (if present)
4. Built-in defaults (see `AppConfiguration`)

Configuration is strongly-typed and validated on startup. If validation fails, a `ConfigurationException` is thrown with aggregated error messages.

### Command-Line Arguments

You can override any configuration setting using command-line arguments with the following formats:

```bash
# Using = syntax
dotnet run --ExcelFilePath="MyData.xlsx" --StaleSeconds=10

# Using space syntax  
dotnet run --ExcelFilePath "MyData.xlsx" --StaleSeconds 10

# Using / syntax (Windows)
dotnet run /ExcelFilePath:"MyData.xlsx" /StaleSeconds:10
```

### Sample `appsettings.sample.json`

```json
{
  "ExcelFilePath": "MarketData.xlsx",
  "WorksheetName": "MarketData",
  "StaleSeconds": 5,
  "BatchHighWatermark": 150,
  "BatchMaxAgeMs": 750,
  "SymbolScanIntervalSeconds": 30,
  "MaxTicksPerSymbol": 100,
  "TickRetentionMinutes": 5,
  "PrioritySymbols": ["YPFD", "GGAL", "PAMP"],
  "EnableReplayLogging": false,
  "ReplayLogPath": null,
  "FeedMode": "Demo",
  "RealFeedProvider": "None"
}
```

Rename to `appsettings.json` to activate.

### Environment Variable Overrides

The application uses .NET's standard configuration providers, which support environment variable overrides using standard naming conventions. Environment variable names should match the configuration property names directly, or use double underscores (`__`) for nested properties.

| Environment Variable | Maps To Configuration | Notes |
|---------------------|----------------------|-------|
| ExcelFilePath | ExcelFilePath | Must end with `.xlsx` |
| WorksheetName | WorksheetName | Primary market data sheet |
| ExcelWriterType | ExcelWriterType | LateBound (default) |
| StaleSeconds | StaleSeconds | 1–300 |
| BatchHighWatermark | BatchHighWatermark | 1–10000 |
| BatchMaxAgeMs | BatchMaxAgeMs | < StaleSeconds*1000 |
| SymbolScanIntervalSeconds | SymbolScanIntervalSeconds | For future scanning extensions |
| MaxTicksPerSymbol | MaxTicksPerSymbol | Retention threshold |
| TickRetentionMinutes | TickRetentionMinutes | Retention window |
| PrioritySymbols | PrioritySymbols | Comma-separated list |
| EnableReplayLogging | EnableReplayLogging | true/false |
| ReplayLogPath | ReplayLogPath | Optional file path |
| LogLevel | LogLevel | One of Trace, Debug, Information, Warning, Error, Critical, None |
| FeedMode | FeedMode | Demo (default) or Real |
| RealFeedEndpoint | RealFeedEndpoint | Required when FeedMode=Real and generic provider |
| RealFeedApiKey | RealFeedApiKey | Required when generic Real provider used |
| RealFeedProvider | RealFeedProvider | None (default) or Primary |
| PrimaryUsername | PrimaryUsername | Required when provider=Primary |
| PrimaryPassword | PrimaryPassword | Required when provider=Primary |
| PrimaryEnvironment | PrimaryEnvironment | e.g. Test / Prod (case-insensitive) |
| PrimaryAccount | PrimaryAccount | Optional (reserved for future order/trade features) |
| Logging__LogLevel__MarketDataExcelUpdater | (logging override) | Debug level for MarketDataExcelUpdater namespace |

### Retention Behavior

Two independent limits constrain per-symbol in-memory tick metadata:

1. MaxTicksPerSymbol (count) – once exceeded, oldest ticks are removed FIFO.
2. TickRetentionMinutes (age) – ticks older than this window are removed.

Eviction runs after each accepted tick; it continues dequeuing while either rule is violated, ensuring post-condition: `count <= MaxTicksPerSymbol` AND `oldestAge <= TickRetentionMinutes`.

Metrics exposed on the `Metrics` sheet:

- `RetentionTotalEvicted`: Cumulative ticks removed.
- `RetentionLastEvictionUtc`: Timestamp (UTC) of last eviction batch.
- `RetentionLastBatchEvicted`: Size of the most recent eviction batch.

If no evictions have occurred, only `RetentionTotalEvicted` (0) may appear.

### Feed Modes & Real Feed Providers

`FeedMode` selects demo vs real integration. When `FeedMode=Real`, a `RealFeedProvider` must be specified.

Providers:

- `None`: (default) If chosen with `FeedMode=Real` validation fails (acts as guard).
- `Primary`: Integrates the Primary.Net market data API via a resilient reflection-based adapter.

Primary-specific config fields (all must pass validation when provider=Primary):

- `PrimaryUsername`
- `PrimaryPassword`
- `PrimaryEnvironment` (e.g., Demo or Prod — value passed through to library if applicable)
- `PrimaryAccount` (optional placeholder for future trading / entitlement features)

#### Primary Integration Notes

The adapter (`PrimaryNetFeedAdapter`):

- Uses reflection against the `Primary.Net` package to avoid tight coupling with evolving beta APIs.
- Performs login, instrument discovery (`GetAllInstruments`), filters to configured `PrioritySymbols`.
- Subscribes to Level 1 bid/ask (depth=1) using `CreateMarketDataSocket`.
- Maps incoming market data to internal `Quote` objects (bid, ask, sizes, timestamp) and dispatches them through the existing pipeline.
- Implements exponential backoff reconnect (2s base, capped at 30s).
- Optionally dumps loaded Primary assemblies when `MDX_PRIMARY_DUMP=1` for troubleshooting.

Because this is reflection-based:

- Minor upstream library changes are less likely to break the build.
- Missing members will generate runtime warnings/errors in logs instead of compile failures.
- Once the API stabilizes, a future refactor can introduce strong typing.

#### Enabling the Primary Feed

Set required environment variables (Windows PowerShell example):

```pwsh
$env:FeedMode = "Real"
$env:RealFeedProvider = "Primary"
$env:PrimaryUsername = "22321456"  # Your username from the logs
$env:PrimaryPassword = "YOUR_ACTUAL_PASSWORD"  # Replace with real password
$env:PrimaryEnvironment = "Prod"
$env:PrioritySymbols = "MERV - XMEV - GGAL - 24hs,MERV - XMEV - PAMP - 24hs,MERV - XMEV - YPFD - 24hs"  # Test with these symbols
$env:Logging__LogLevel__MarketDataExcelUpdater = "Debug"
```

Then run:

```pwsh
dotnet run --project src\MarketDataExcelUpdater
```

If credentials are valid and symbols resolve, bid/ask updates will flow into `MarketData.xlsx`.

To inspect library metadata, create a diagnostic environment variable that the Primary provider can check (implementation detail).

Run again and review log lines for loaded assemblies.

### Demo Feed

Enabled by default when `FeedMode=Demo` (default setting). Generates pseudo-random quotes for configured `PrioritySymbols` (or fallback list). Each symbol maintains an increasing sequence number.

### Graceful Shutdown

- Ctrl+C triggers cancellation
- Orchestrator loop stops
- Final flush occurs if pending updates
- All disposable resources cleaned up

### Testing Strategy

- Pure domain logic (batching, stale monitor, sequence tracking) covered with deterministic unit tests.
- Configuration loader tests cover defaults, JSON merge, env overrides, validation.
- Real provider integration currently exercised indirectly; future work may add contract tests around reflection mapping.

### Architectural Overview

```text
[Demo / Primary Feed] -> TickDispatcher -> UpdateBatch -> FlushOrchestrator -> IExcelWriter(LateBound)
                           ^          |
                  StaleMonitor <------+ 
```

- `TickDispatcher`: Maintains instruments & assembles cell updates
- `FlushOrchestrator`: Time/count-based flush loop
- `ExcelLateBoundWriter`: Materializes batches into Excel; auto-creates sheets/columns
- `AppConfiguration`: Strongly-typed config with validation

### Extensibility Ideas

- Transition Primary adapter to strong typing when API stabilizes
- Add additional providers (e.g., REST→WebSocket bridges) by implementing `IMarketDataFeed`
- Retention & stale visual cues in Excel (conditional formatting)
- Metrics export (Prometheus / CSV)
- Switch to `GenericHost` for standardized hosting & DI
- Sequence gap recovery strategies (request missed ticks)
- Historical backfill / snapshot loading

### Troubleshooting

| Symptom | Possible Cause | Resolution |
|---------|----------------|-----------|
| Workbook not created | No write permission | Run from writable directory / adjust path |
| No data appearing | Real feed not configured | Set `FeedMode=Demo` or configure real feed environment variables |
| Configuration exception | Invalid JSON or environment values | Fix errors listed in exception message |
| High flush frequency | Low `BatchHighWatermark` | Increase environment variable or JSON setting |
| Primary login failing | Wrong credentials or environment | Verify `PrimaryUsername`, `PrimaryPassword`, `PrimaryEnvironment` |
| Missing symbols | Symbol not in provider universe | Adjust `PrioritySymbols` / verify listing |
| No Primary quotes but connected | API shape change | Check logs and update adapter mapping if needed |
| "Could not load Excel application" | Excel not installed or COM issue | Install Microsoft Excel and ensure proper COM registration |
| LateBound writer fails | Excel COM registration issue | Try running Excel as administrator once or reinstalling Office |
| Repeated Excel write failures | Excel busy/locked, COM issues | Application will use exponential backoff (up to 30s), market data continues flowing |
| COM Exception 0x800A01A8 | Excel worksheet/cell access issue | Temporary issue - exponential backoff will retry when Excel is available |
| Excel writes stop but data flows | Excel in backoff period | Normal behavior - Excel writes resume automatically when backoff period expires |

### License

Internal / TBD.

---
Generated as part of Phase G deliverables (with Primary provider integration).
