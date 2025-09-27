# IBatchPolicy Contract

## Purpose

Determines when pending Excel updates should be flushed.

## Methods

```csharp
bool ShouldFlush(DateTime nowArt, int pendingCount, DateTime oldestPendingEnqueueTime);
```

## Behavior

- Return true if pendingCount >= HighWatermark (default 50) OR (now - oldest) >= MaxAge (default 200ms).
- Priority symbols (config) may force flush immediately.

## Configuration

- HighWatermark: int (default 50)
- MaxAgeMs: int (default 200)
- PrioritySymbols: set&lt;string&gt;

## Failure Modes

- If oldestPendingEnqueueTime > nowArt (clock skew) → ignore age condition this cycle.
