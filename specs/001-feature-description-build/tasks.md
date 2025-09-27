# Tasks: Near Real-Time Argentine Market → Excel Updater

Generated: 2025-09-25
Source Spec: `specs/001-feature-description-build/spec.md`
Plan: `specs/001-feature-description-build/plan.md`
Constitution: `.specify/memory/constitution.md`

Legend:

- [P] = Potentially parallel (independent of ordering except where noted)
- Gate = Quality / architectural checkpoint
- Test IDs reference FR-* requirements where applicable

## Phase A: Foundational Contracts & Models (TDD First)

1. Task A1: Create solution + projects skeleton (src + tests) [Gate prerequisite]
1. Task A2: Add base Domain models (`MarketInstrument`, `Quote`, `ExcelRowBinding`, `UpdateBatch`, `CellUpdate`, `MetricsSnapshot`) with minimal properties (no behavior) [P]
1. Task A3: Define interfaces in code for `IMarketDataSubscriber`, `IExcelWriter`, `IStaleMonitor`, `IBatchPolicy` based on contracts markdown [P]
1. Task A4: Add test utilities project/folder (e.g., TestDoubles) with placeholder mocks [P]
1. Task A5: Write unit tests for `IStaleMonitor` behavior (fresh→stale, stale→fresh) (FR-007)
1. Task A6: Write unit tests for `IBatchPolicy` flush decisions (FR-010)
1. Task A7: Write unit tests for `MarketInstrument` state update invariants (timestamp monotonic, negative values ignored) (FR-003, FR-004)
1. Task A8: Write failing contract tests for `IExcelWriter` (QueueUpdate O(1), EnsureColumns adds missing) (FR-017, FR-010)
1. Task A9: Write failing contract tests for `IMarketDataSubscriber` (subscribe union, unknown symbol ignored) (FR-002, FR-012)
1. Task A10: Gate A - Ensure all above tests fail for expected NotImplemented exceptions (sanity)
1. Task A11: Add sequence counter to MarketInstrument + failing tests for monotonic increment & gap placeholder (FR-013, FR-030)
1. Task A12: Add ReplayHarness + failing replay test ingesting sample tick JSON (FR-028)

## Phase B: Core Implementations

1. Task B1: Implement `StaleMonitor` to pass A5 (dictionary timestamps, threshold configurable) (FR-007)
1. Task B2: Implement `BatchPolicy` (configurable HighWatermark/MaxAge/PrioritySymbols) (FR-010)
1. Task B3: Implement `MarketInstrument` update method enforcing invariants (FR-003, FR-004)
1. Task B4: Implement in-memory `ExcelWriter` (no actual Excel yet) that records queued updates & simulates column ensuring for tests (FR-017)
1. Task B5: Implement `MarketDataSubscriber` fake adapter (simulated events) to satisfy contract tests; real provider deferred (FR-002)
1. Task B6: Add configuration objects & validation (staleSeconds, batchHighWatermark, batchMaxAgeMs, symbolScanIntervalSeconds) (FR-014, FR-025)
1. Task B7: Gate B - All Phase A tests passing + new implementation tests added
1. Task B8: Implement gap detection & 3-attempt resync (FR-013, FR-030)
1. Task B9: Implement retention (100 ticks / 5 min) & eviction trimming tests (FR-016, FR-026)
1. Task B10: Implement numeric formatting utility & tests (FR-029)

## Phase C: Excel Integration (ClosedXML)

1. Task C1: Integrate ClosedXML package; implement real `ClosedXmlExcelWriter` adapting contract (FR-003, FR-017)
1. Task C2: Add column auto-create logic (append canonical order) (FR-017)
1. Task C3: Implement batched flush applying cell updates (optimize to single SaveChanges style) (FR-010)
1. Task C4: Add numeric formatting preservation tests (FR-021)
1. Task C5: Gate C - Latency micro-benchmark for flush path (<50ms p95 synthetic) (FR-003)
1. Task C6: Implement latency histogram & flush latency metrics (FR-019, FR-030)
1. Task C7: Implement clock drift detection + throttled warning (FR-027)
1. Task C8: Extraneous column protection test (FR-009)
1. Task C9: Variant differentiation test (base vs settlement) (FR-005)

## Phase D: Pipeline & Orchestration

1. Task D1: Implement Tick Dispatcher: On quote → update model → queue cell diffs → mark stale monitor
1. Task D2: Implement Flush Orchestrator loop (checks IBatchPolicy, triggers FlushAsync) (FR-010)
1. Task D3: Implement Symbol Scanner (periodic new symbol detection) (FR-006, FR-014)
1. Task D4: Implement Stale evaluation loop (every second) writing stale flags column (FR-007)
1. Task D5: Implement Heartbeat metrics row updates (FR-019)
1. Task D6: Structured logging basic events emission: start, subscribe, reconnect, stale, write failure, duplicate (FR-008, FR-012)
1. Task D7: Graceful shutdown handling (Ctrl+C) releasing resources (FR-015)
1. Task D8: Gate D - End-to-end in-memory integration test passes (stories 1–4)
1. Task D9: Heartbeat cadence test (simulate time) (FR-019)
1. Task D10: Manual workbook save concurrency test (FR-018, FR-023)
1. Task D11: Duplicate symbol single-warning test (FR-012)
1. Task D12: Structured log schema tests (gap, evict, drop, retry) (FR-030)

## Phase E: Provider & Resilience

1. Task E1: Implement Primary.Net adapter behind `IMarketDataSubscriber` with reconnect/backoff (FR-002, FR-011)
1. Task E2: Add transient error retry logic (up to 3) (FR-011)
1. Task E3: Duplicate symbol detection & logging at startup and on scan (FR-012)
1. Task E4: Stale detection logging & counters (FR-007, FR-019)
1. Task E5: Network drop simulation test (FR-011)
1. Task E6: Gate E - Network resilience tests green
1. Task E7: Gap resync failure path test (unresolved gap ERROR) (FR-013)
1. Task E8: Dropped tick reason classification tests (parse error, out-of-order, gap) (FR-030)

## Phase F: Performance & Polishing

1. Task F1: Performance test: sustained 200 ticks/sec for 60s (FR-003, FR-010)
1. Task F2: Memory profiling (<100MB RSS under load) (Constraint)
1. Task F3: Add configuration documentation to `quickstart.md` (FR-025)
1. Task F4: Add README with architecture diagram & Constitution mapping
1. Task F5: Gate F - All functional requirements validated & metrics row displays correct counters
1. Task F6: Backlog burst/drain test (burst 200 ticks/sec then idle; drain ≤2s) (FR-016)
1. Task F7: Memory retention verification under sustained load (FR-016, FR-026)
1. Task F8: Dependency justification audit check (ensures FR-031 compliance)
1. Task F9: Replay regression final state verification (FR-028)

## Phase G: Deferred / Optional Enhancements

1. Task G1: Add turnover session reset semantics if clarified
1. Task G2: Add optional Prometheus exporter (Principle V gate review)
1. Task G3: Add Excel UI ribbon button to toggle updater (out of scope v1)

## Quality Gates Mapping

| Gate | Purpose | Blocks Progress Until |
|------|---------|------------------------|
| A | Contracts & models defined, failing tests in place | Tests exist & failing intentionally |
| B | Core logic implemented | All Phase A tests pass |
| C | Real Excel integration validated | Latency micro benchmark passes |
| D | End-to-end basic pipeline works | User story tests pass |
| E | Provider resilience works | Network tests pass |
| F | Performance & docs complete | Performance & memory targets met |

## Requirement Coverage Traceability

| FR | Tasks |
|----|-------|
| FR-001 | D3 |
| FR-002 | A9, B5, E1 |
| FR-003 | A7, B3, C1, C3, F1 |
| FR-004 | A7, B3 |
| FR-005 | (implicit in symbol handling) D1, D3 |
| FR-006 | D3 |
| FR-007 | A5, B1, D4, E4 |
| FR-008 | D6 |
| FR-009 | C1, C2 |
| FR-010 | A6, B2, C3, D2, F1 |
| FR-011 | E2, E5 |
| FR-012 | A9, E3 |
| FR-013 | (Documented omission) |
| FR-014 | D3, B6 |
| FR-015 | D7 |
| FR-016 | F1, F2 |
| FR-017 | A8, C2 |
| FR-018 | C3 (non-blocking writes) |
| FR-019 | D5, E4 |
| FR-021 | C4 |
| FR-022 | (No resort) D1 |
| FR-023 | D7 |
| FR-024 | B3 (leave blanks) |
| FR-025 | B6, F3 |
| FR-026 | B9, F7 |
| FR-027 | C7 |
| FR-028 | A12, F9 |
| FR-029 | B10 |
| FR-030 | A11, B8, D12, E7, E8, C6 |
| FR-031 | F8 |

## Notes

- Parallelizable tasks marked [P] can be executed in any order after A1.
- Gates enforce constitutional simplicity; avoid adding dependencies before Gate C.
- Optional enhancements require new justification per Constitution Principle V.
