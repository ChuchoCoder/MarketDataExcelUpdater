<!--
Sync Impact Report
Version change: template → 1.0.0
Modified principles: (placeholders replaced with concrete definitions)
Added sections: Performance & Data Constraints; Development Workflow & Quality Gates
Removed sections: none
Templates requiring updates:
 - .specify/templates/plan-template.md ✅ (version reference updated to v1.0.0)
 - .specify/templates/spec-template.md ✅ (no conflicting guidance)
 - .specify/templates/tasks-template.md ✅ (already enforces tests-first)
 - .specify/templates/agent-file-template.md ⚠ pending (auto-generated later; will inherit principles summary in future regeneration)
Follow-up TODOs: none (no unresolved placeholders)
-->

# MarketDataExcelUpdater Constitution

## Core Principles

### I. Data Integrity & Market Accuracy (NON-NEGOTIABLE)

All Excel outputs MUST reflect the latest confirmed exchange tick for each subscribed Argentinian market instrument (equities, bonds, options, futures) without speculative extrapolation. Invalid, malformed, or stale (>5s since last tick) data MUST be discarded and logged. Instrument identifiers MUST follow a canonical schema (Market:Symbol:ContractMonth when applicable) and any transformation MUST be deterministic and reversible. No aggregation that loses the ability to reconstruct the original tick is permitted. Data written to Excel MUST include a monotonically increasing sequence number per instrument to detect gaps.

Rationale: Trading and analytical decisions rely on exact tick fidelity; silent drift or silent gap acceptance creates hidden risk.

### II. Low-Latency Simplicity

End-to-end tick reception to Excel cell update p95 latency MUST remain <500ms; p99 <1000ms under normal market load (≤ 200 ticks/sec aggregate). Implementation MUST prioritize O(1) per-tick operations: direct parsing → validation → in-memory map update → Excel write (batched if >50 updates inside a 200ms window). No message brokers, complex caching layers, or premature microservice splits until sustained load exceeds defined thresholds (p95 >500ms for 3 consecutive trading sessions). Each added abstraction MUST document a measurable latency or maintainability improvement (>15% latency reduction or removal of ≥50 LOC).

Rationale: Minimal moving parts reduce latency variance and reduce operational risk.

### III. Test-First Reliability

Before implementing a data transformation, a failing test MUST exist (unit for pure transforms, contract for provider API shape, integration for multi-step flows). Replay tests MUST load recorded tick snapshots and assert identical Excel cell outputs and sequence continuity. Any bug fix MUST include a regression test reproducing the failure first. No code may merge with red tests or without at least one replay scenario covering a trading session open burst. Critical validation rules (stale detection, sequence gap detection) MUST have explicit negative tests.

Rationale: Deterministic replay + TDD prevents latent data corruption and ensures refactors remain safe.

### IV. Observability & Transparency

Structured logs (JSON) MUST emit at: connection open/close, authentication, subscription change, tick gap detected, stale instrument eviction, Excel write failures, and recovery actions. Metrics MUST include: ticks_received_total, ticks_written_total, dropped_ticks_total (with reason labels), latency_ms histogram, active_instruments, reconnect_count. A heartbeat row or sheet cell MUST update every 10s with last tick time and counts. Any Excel write error MUST produce an ERROR log and non-zero process exit if unrecoverable. No silent retries beyond 3 attempts with exponential backoff (base 200ms). Alert conditions (gap >3 sequence numbers, reconnect storm >5/min) MUST be detectable from metrics alone.

Rationale: Fast incident triage demands precise signals; silence hides integrity issues.

### V. Minimal Dependencies & Maintainability

Only widely adopted, security-supported libraries MAY be added; each new dependency MUST justify itself by either providing protocol access we cannot reasonably build (<1 day) or removing ≥30 lines of internal code. Functions SHOULD target ≤50 LOC; modules SHOULD have single responsibility (data feed, validation, Excel IO, metrics, orchestration). Cyclic imports are forbidden. Complexity (new threads, async loops, caching layers) MUST include a measured benefit statement in the PR description. Dead code removal is prioritized every release (no unused functions). Lint and static analysis MUST run clean before merge.

Rationale: A lean stack reduces upgrade risk, speeds comprehension, and minimizes failure surface.

## Performance & Data Constraints

Latency Targets: p95<500ms, p99<1000ms (tick ingress → Excel write). Throughput Baseline: Sustain 200 ticks/sec without backlog; backlog processing MUST drain within 2s after bursts. Memory Footprint: In-memory tick store limited to last 100 ticks per instrument OR 5 minutes, whichever smaller. Staleness Detection: Instrument flagged stale at 5s of silence; evicted at 30s (log event). Sequence Gaps: Gap >1 triggers immediate resync request or reconnect; unresolved after 3 attempts escalates error. Excel Write Policy: Batch writes if >50 pending updates or >200ms since last flush; NEVER delay single high-value instrument (configurable priority list). Time Synchronization: System clock MUST sync via NTP daily; drift >100ms triggers warning log. Validation: Each constraint MUST map to at least one automated test or metric alert definition.

## Development Workflow & Quality Gates

Branching: Feature branches prefixed with numeric ticket or date slug. Every PR MUST list affected principles (e.g., Principles: I, II) and note any potential latency impact. Code Review: At least one reviewer MUST explicitly confirm "Principles preserved"; absence fails the gate. Quality Gates (CI): (1) All tests green (unit, integration, replay). (2) Coverage ≥80% lines overall; ≥100% for pure transform modules. (3) Latency micro-benchmark suite runs and reports compliance (fail if regression >10% p95). (4) Lint & static analysis pass. (5) Constitution reference version matches latest (1.0.0 currently). Adding a Data Source: Requires spec (entities, field mapping, validation rules), contract tests, replay sample file, and performance impact note. Naming: Modules use snake_case; metrics use snake.case.with.dots for hierarchy. Commits: If altering performance-critical path, include tag [perf]. Any TODO must reference an issue ID; untracked TODO blocks merge. Documentation: Quickstart includes example Excel sheet layout + metric interpretation table, updated if schema changes.

## Governance

Authority: This constitution supersedes ad hoc practices. Conflicts are resolved in favor of stricter integrity or simplicity rules.

Amendments: Submit PR with redline diff of changed sections + explicit version bump rationale (Patch=clarification, Minor=new principle/constraint, Major=breaking removal or semantic rewrite). Require approval from at least one maintainer NOT author of change. On merge, update version line and date.

Review Cadence: Formal review monthly or within 48h after major exchange API change or sustained latency breach.

Compliance Verification: CI job validates presence of principle references in PR description, checks latency test report, ensures no forbidden dependencies. Violations require either refactor or an approved, time-bounded Exception Record (expires ≤30 days) stored in /docs/exceptions/.

Versioning Policy: Semantic governance versioning (MAJOR.MINOR.PATCH) as defined above. Each release MUST include a short CHANGELOG excerpt summarizing principle-impacting changes. Breaking governance changes require migration notes.

Enforcement: Repeated violation (≥2 merged exceptions without remediation) triggers mandatory architecture review before further feature merges.

Sunset & Deletions: Removing a principle triggers at least a Minor bump (or Major if redefining scope of other principles) and must list compensating controls.

**Version**: 1.0.0 | **Ratified**: 2025-09-25 | **Last Amended**: 2025-09-25
