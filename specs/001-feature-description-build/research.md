# Research Summary

Date: 2025-09-25  
Feature: Near Real-Time Argentine Market → Excel Updater  

## Decisions

### Excel Write Strategy

- Decision: Use Late-Bound COM Excel interop via dynamic dispatch.
- Rationale: Version-agnostic approach that works with any installed Excel version, provides direct live workbook interaction without assembly version conflicts, supports real-time cell updates for open workbooks.
- Alternatives Considered: Other Excel libraries were evaluated but rejected due to file locking issues or lack of real-time update support for open workbooks.

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
| COM interop stability under sustained load | Medium | Implement robust error handling and COM object lifecycle management |
| Primary.Net reconnect semantics unknown | Medium | Add retry/backoff; log exhaustive reconnect metrics |
| Excel application process crash/restart | Medium | Detect Excel availability and reconnect automatically |
| Time drift causing misleading timestamps | Low | Add discrepancy log when >2s offset |

## Summary
Research complete; no blocking unknowns; proceed to Phase 1 design.
