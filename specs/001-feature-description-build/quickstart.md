# Quickstart: Excel Market Data Updater

## Prerequisites

- Windows with Excel installed
- .NET 9 SDK
- MarketData workbook with sheet `MarketData` and at minimum header row containing: symbol, bid, ask, last, datetime

## Run (Planned)

1. Build solution.
2. Open Excel workbook.
3. Run updater: `dotnet run -- symbols=GGAL,BMA,YPFD` (example CLI to be finalized).
4. Observe cells updating within 1s p95.

## Heartbeat

- Bottom row (or dedicated row) shows `__heartbeat__` and metrics (ticks_received_total, stale_count).

## Adding Symbols

- Append symbol in first empty row under `symbol` column.
- Save workbook; updater begins updating within 10s.

## Staleness

- Stale column (TRUE/FALSE) becomes TRUE after 5s silence; resets on next tick.

## Shutdown

- Ctrl+C triggers graceful unsubscribe, final flush, and exit code 0.

## Troubleshooting

| Symptom | Possible Cause | Action |
|---------|----------------|--------|
| No updates | Sheet name mismatch | Rename sheet to `MarketData` or configure |
| Cells slow to update | Batch threshold too high | Reduce batch size in config |
| Stale never clears | No new ticks arriving | Verify symbol validity |
| Column missing | Auto-create failed | Check logs for column creation list |
