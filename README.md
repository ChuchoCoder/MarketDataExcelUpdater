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
set MDX_EXCELWRITERTYPE=LateBound
```

### Usage Benefits

The LateBound writer provides several advantages:

- **Version Agnostic**: Works with any Excel installation without assembly version conflicts
- **Real-time Updates**: Live updates visible while Excel is open
- **Interactive Analysis**: Formulas and charts update in real-time
- **No Assembly Dependencies**: Uses late-bound COM to avoid version-specific references
- **Reliable Live Integration**: Similar to xlwings functionality for Python

## Configuration

Configuration is loaded in layers:

1. Internal defaults (see `AppConfiguration`)
2. Optional JSON file `appsettings.json` (or explicit path passed to loader)
3. Environment variable overrides prefixed with `MDX_`

If validation fails a `ConfigurationException` is thrown with aggregated error messages.

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

| Env Var | Maps To | Notes |
|--------|---------|-------|
| MDX_EXCELFILEPATH | ExcelFilePath | Must end with `.xlsx` |
| MDX_WORKSHEETNAME | WorksheetName | Primary market data sheet |
| MDX_EXCELWRITERTYPE | ExcelWriterType | LateBound (default) |
| MDX_STALESECONDS | StaleSeconds | 1–300 |
| MDX_BATCH_HIGHWATERMARK | BatchHighWatermark | 1–10000 |
| MDX_BATCH_MAXAGEMS | BatchMaxAgeMs | < StaleSeconds*1000 |
| MDX_SYMBOLSCANINTERVALSECONDS | SymbolScanIntervalSeconds | For future scanning extensions |
| MDX_MAXTICKSPERSYMBOL | MaxTicksPerSymbol | Retention threshold |
| MDX_TICKRETENTIONMINUTES | TickRetentionMinutes | Retention window |
| MDX_PRIORITY_SYMBOLS | PrioritySymbols | Comma-separated list |
| MDX_ENABLEREPLAYLOGGING | EnableReplayLogging | true/false |
| MDX_REPLAYLOGPATH | ReplayLogPath | Optional file path |
| MDX_DISABLE_DEMO | (no config field) | When `1`, disables synthetic feed |
| MDX_LOGLEVEL | LogLevel | One of Trace, Debug, Information, Warning, Error, Critical, None |
| MDX_FEEDMODE | FeedMode | Demo (default) or Real |
| MDX_REALFEED_ENDPOINT | RealFeedEndpoint | Required when FeedMode=Real and generic provider |
| MDX_REALFEED_APIKEY | RealFeedApiKey | Required when generic Real provider used |
| MDX_REALFEED_PROVIDER | RealFeedProvider | None (default) or Primary |
| MDX_PRIMARY_USERNAME | PrimaryUsername | Required when provider=Primary |
| MDX_PRIMARY_PASSWORD | PrimaryPassword | Required when provider=Primary |
| MDX_PRIMARY_ENVIRONMENT | PrimaryEnvironment | e.g. Demo / Prod (case-insensitive) |
| MDX_PRIMARY_ACCOUNT | PrimaryAccount | Optional (reserved for future order/trade features) |
| MDX_PRIMARY_DUMP | (diagnostic) | When `1`, logs discovered Primary.* assemblies |

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

Set required environment variables (Windows `cmd.exe` example):

```pwsh
$env:MDX_FEED_MODE = "Real"
$env:MDX_REAL_FEED_PROVIDER = "Primary"
$env:MDX_PRIMARY_USERNAME = "22321456"  # Your username from the logs
$env:MDX_PRIMARY_PASSWORD = "YOUR_ACTUAL_PASSWORD"  # Replace with real password
$env:MDX_PRIMARY_ENVIRONMENT = "Prod"
$env:MDX_SYMBOLS = "MERV - XMEV - GGAL - 24hs,MERV - XMEV - PAMP - 24hs,MERV - XMEV - YPFD - 24hs"  # Test with these symbols
$env:Logging__LogLevel__MarketDataExcelUpdater = "Debug"
```

Then run:

```pwsh
dotnet run --project src\MarketDataExcelUpdater
```

If credentials are valid and symbols resolve, bid/ask updates will flow into `MarketData.xlsx`.

To inspect library metadata:

```pwsh
$env:MDX_PRIMARY_ENVIRONMENT = 1
```

Run again and review log lines for loaded assemblies.

### Demo Feed

Enabled by default. Generates pseudo-random quotes for configured `PrioritySymbols` (or fallback list). Disable via `MDX_DISABLE_DEMO=1`. Each symbol maintains an increasing sequence number.

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
| No data appearing | Demo disabled + Real not configured | Set `MDX_FEEDMODE=Demo` or provide real env vars |
| Validation exception | Bad JSON or env values | Fix errors listed in exception message |
| High flush frequency | Low `BatchHighWatermark` | Increase env var or JSON setting |
| Primary login failing | Wrong credentials or environment | Verify `MDX_PRIMARY_*` vars and environment name |
| Missing symbols | Symbol not in provider universe | Adjust `MDX_PRIORITY_SYMBOLS` / verify listing |
| No Primary quotes but connected | API shape change | Enable `MDX_PRIMARY_DUMP=1` & check logs; update adapter mapping |
| "Could not load Excel application" | Excel not installed or COM issue | Install Microsoft Excel and ensure proper COM registration |
| LateBound writer fails | Excel COM registration issue | Try running Excel as administrator once or reinstalling Office |

### License

Internal / TBD.

---
Generated as part of Phase G deliverables (with Primary provider integration).
