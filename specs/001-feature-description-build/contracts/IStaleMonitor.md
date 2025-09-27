# IStaleMonitor Contract

## Purpose

Tracks last update timestamps per symbol and determines stale set.

## Methods

```csharp
void MarkUpdate(string symbol, DateTime timestampArt);
IReadOnlyCollection<string> GetStaleSymbols(DateTime nowArt);
```

## Behavior

- MarkUpdate refreshes internal timestamp map.
- GetStaleSymbols returns symbols where (now - last) > configured threshold (default 5s).
- Transition Fresh→Stale triggers log event (caller handles logging via diffing).

## Performance

- O(1) updates (dictionary), O(n) scan per invocation (n = symbols) acceptable at 200 scale.

## Failure Modes

- Unknown symbol on MarkUpdate: ignored.
