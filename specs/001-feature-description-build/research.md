# Research Summary

Date: 2025-09-25  
Feature: Near Real-Time Argentine Market → Excel Updater  

## Decisions

### Excel Write Strategy

- Decision: Use ClosedXML for initial implementation.
- Rationale: Simpler object model, avoids COM interop threading issues, supports batch cell updates in-memory before Save()/Write to stream; acceptable latency for ~200 symbol updates.
- Alternatives:
  - Microsoft.Office.Interop.Excel: Pros: Direct live workbook interaction; Cons: COM complexity, STA threading, higher risk of hangs.
  - EPPlus: Strong API but additional licensing considerations for some use; considered but ClosedXML sufficient.

### Market Data Subscription via Primary.Net

- Decision: Wrap Primary.Net in `IMarketDataSubscriber` interface exposing Subscribe(symbols) and QuoteReceived event.
- Rationale: Isolation for test mocking and future provider swap.
- Alternatives: Direct static usage of Primary.Net types (rejected: harder to test).

### Batching Algorithm

- Decision: Hybrid: flush when (pending >=50) OR (oldest pending age >=200ms) OR (tick for priority symbol arrives) whichever first.
- Rationale: Matches constitution batching rule and latency goals.
- Alternatives: Fixed interval (risk latency spikes), per-tick immediate write (higher overhead).

### Metrics Emission

- Decision: Simple in-process counters + periodic (10s) heartbeat updater; no external metrics dependency initially.
- Rationale: Keep dependencies minimal (Principle V) while satisfying observability.
- Alternatives: Prometheus-net (overhead, not yet needed), OpenTelemetry (overkill early).

### Heartbeat Row Approach

- Decision: Dedicated row at bottom of `MarketData` sheet with fields: `__heartbeat__`, last_tick_time, ticks_received_total, stale_count, reconnect_count.
- Rationale: Visible health indicator; minimal intrusion.
- Alternatives: Separate sheet (slightly more overhead, not needed yet).

### Clock & Time Sync

- Decision: Rely on OS NTP sync + log warning if tick timestamps differ from local ART by >2s.
- Rationale: Simplicity; deeper drift detection can be added later.
- Alternatives: Implement NTP query in app (extra complexity not justified yet).

## Open (Deferred) Items

- Turnover session reset semantics (assume no daily reset until specified).
- Potential future metrics export endpoint (defer until external monitoring required).

## Test Harness Strategy

- Replay: Store JSON lines of synthetic tick feed; feed into pipeline; assert Excel model state (not actual file) before writer layer.
- Performance micro-bench: Benchmark batching flush function and stale monitor scanning.

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| ClosedXML latency spikes under heavy churn | Medium | Measure p95/p99; fallback to Interop if needed |
| Primary.Net reconnect semantics unknown | Medium | Add retry/backoff; log exhaustive reconnect metrics |
| Excel file lock/contention | Low | Keep writes batched; open workbook read/write once |
| Time drift causing misleading timestamps | Low | Add discrepancy log when >2s offset |

## Summary
Research complete; no blocking unknowns; proceed to Phase 1 design.
