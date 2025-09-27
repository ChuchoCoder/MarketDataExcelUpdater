# IMarketDataSubscriber Contract

## Purpose

Abstracts Primary.Net subscription to enable mocking and potential provider swap.

## Methods

```csharp
void Subscribe(IEnumerable<string> symbols);
void Unsubscribe(IEnumerable<string> symbols); // optional for v1
```

## Events

```text
Event: QuoteReceived(QuoteEvent e)
  e.Symbol : string
  e.Bid / e.Ask / e.Last / ... : nullable decimals
  e.EventTime : DateTime (ART)
```

## Behavior

- Multiple Subscribe calls extending set = union (idempotent).
- Receiving quote for unknown symbol → ignored + WARN log.
- Reconnect logic internal; emits reconnect log events.

## Failure Modes

- Network drop: raises internal retry (exponential backoff base 200ms, max 3 attempts) before surfacing ERROR.
- Malformed payload: increments DroppedTicks with reason=ParseError.
