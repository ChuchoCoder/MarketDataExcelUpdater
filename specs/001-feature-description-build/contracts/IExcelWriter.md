# IExcelWriter Contract

## Purpose

Provides batched, low-latency writes of instrument quote fields to an open Excel workbook.

## Methods

```csharp
void QueueUpdate(string symbol, string field, object? value);
Task FlushAsync(CancellationToken ct = default);
void EnsureColumns(IEnumerable<string> requiredHeaders);
```

## Behavior

- QueueUpdate is non-blocking O(1), stores cell intents.
- FlushAsync applies all pending updates in a single batch.
- EnsureColumns auto-creates missing required headers appended in canonical order.

## Performance

- Expected batch flush latency <50ms p95 for 200 symbols.
- No individual QueueUpdate triggers Excel recalculation.

## Failure Modes

- Workbook closed: FlushAsync throws; caller logs ERROR & triggers graceful shutdown.
- Invalid column name: ignored + WARN.
