# Feature Specification: Near Real-Time Argentine Market → Excel Updater

**Feature Branch**: `001-feature-description-build`  
**Created**: 2025-09-25  
**Status**: Draft  
**Input**: User description: "Build an application that can help me obtain near realtime data from the Argentinean market and update an Excel spreadsheet. This updater app will need to update the sheet 'MarketData' where there we have the entire list of symbols that we want to obtain the data and update the corresponding column/cell with the market information. Example columns: symbol, bid_size, bid, ask, ask_size, last, change, open, high, low, previous_close, turnover, volume, operations, datetime. Include both spot and different settlement terms if applicable (e.g., 24hs)."

## Clarifications

### Session 2025-09-25

- Q: How should stale instruments be represented after 5s without an update? → A: Add boolean Stale column (TRUE/FALSE)
- Q: How should duplicate symbols be handled when the same symbol appears multiple times in the sheet? → A: Update only the first occurrence; leave others unchanged (warning logged)
- Q: How will we detect and handle quote sequencing / gaps? → A: Ignore gap detection (assume provider reliability; no synthetic counter for gap logic)
- Q: What timezone should be used for the `datetime` column? → A: Argentina Time (ART, UTC-3) displayed directly (no separate UTC column)
- Q: How should missing required header columns be handled at startup? → A: Auto-create any missing required columns (appended in canonical order at end) and proceed (log INFO once)

## Execution Flow (main)

```text
1. Parse user description from Input
   → If empty: ERROR "No feature description provided"
2. Extract key concepts from description
   → Identify: actors, actions, data, constraints
3. For each unclear aspect:
   → Mark with [NEEDS CLARIFICATION: specific question]
4. Fill User Scenarios & Testing section
   → If no clear user flow: ERROR "Cannot determine user scenarios"
5. Generate Functional Requirements
   → Each requirement must be testable
   → Mark ambiguous requirements
6. Identify Key Entities (if data involved)
7. Run Review Checklist
   → If any [NEEDS CLARIFICATION]: WARN "Spec has uncertainties"
   → If implementation details found: ERROR "Remove tech details"
8. Return: SUCCESS (spec ready for planning)
```

---

## ⚡ Quick Guidelines

- ✅ Focus on WHAT users need and WHY
- ❌ Avoid HOW to implement (no tech stack, APIs, code structure)
- 👥 Written for business stakeholders, not developers

### Section Requirements

- **Mandatory sections**: Must be completed for every feature
- **Optional sections**: Include only when relevant to the feature
- When a section doesn't apply, remove it entirely (don't leave as "N/A")

### For AI Generation

When creating this spec from a user prompt:

1. **Mark all ambiguities**: Use [NEEDS CLARIFICATION: specific question] for any assumption you'd need to make
2. **Don't guess**: If the prompt doesn't specify something (e.g., "login system" without auth method), mark it
3. **Think like a tester**: Every vague requirement should fail the "testable and unambiguous" checklist item
4. **Common underspecified areas**:
   - User types and permissions
   - Data retention/deletion policies  
   - Performance targets and scale
   - Error handling behaviors
   - Integration requirements
   - Security/compliance needs

---

## User Scenarios & Testing *(mandatory)*

### Primary User Story

As a market analyst / trader, I want an Excel workbook sheet (`MarketData`) to stay updated within seconds with the latest quotes for a configured list of Argentine market instruments (equities, bonds, options, futures, spot vs 24hs settlement variants) so that I can perform analysis, monitor movements, and make decisions without manually refreshing or exporting data.

### Acceptance Scenarios

1. **Given** the Excel workbook is open with a sheet named `MarketData` containing a header row with required columns and a list of symbols in the `symbol` column, **When** the updater starts, **Then** each listed symbol row begins receiving updated field values (bid, ask, last, etc.) within <5s of start.
2. **Given** a symbol receives a new tick with changed bid/ask/last, **When** latency is normal, **Then** the corresponding cells update within 1 second (p95) of tick receipt.
3. **Given** a symbol is no longer trading or no tick is received for >5s, **When** staleness rules apply, **Then** the row's datetime cell reflects last update time and (optionally) a staleness indicator column (if added) is marked while existing values remain unchanged.
4. **Given** a new symbol is added to the bottom of the `symbol` column while the updater is running, **When** the sheet is saved, **Then** the updater detects and starts updating that new row within 10 seconds.
5. **Given** an invalid or unsupported symbol is present, **When** the updater attempts subscription, **Then** it logs an error and the row remains blank (non-price columns unchanged) without halting other updates.
6. **Given** network connectivity temporarily drops, **When** it is restored within 30 seconds, **Then** the updater automatically reconnects and resumes updates without duplicate row insertion.
7. **Given** both spot and 24hs variants of an instrument (e.g., GGAL vs GGAL-24hs) are listed, **When** ticks arrive, **Then** each row independently updates with its correct market variant values.

### Edge Cases

- Workbook closed unexpectedly while updater runs → graceful stop & warning log.  
- Excel sheet renamed from `MarketData` → updater detects absence, emits error, and retries detection periodically.  
- Duplicate symbol rows → only the first occurrence updates; subsequent duplicates remain unchanged; warning logged at startup and on detection.  
- Extremely high tick burst (>200 ticks/sec) → backlog risk; updater MUST batch writes without exceeding 1s latency p99.  
- Column missing (e.g., `turnover`) → Auto-create any missing required header columns at startup (logged once) and continue.  
- Day rollover / market session boundary → confirm whether to reset daily metrics like turnover [NEEDS CLARIFICATION: turnover semantics per session?].  
- Time zone handling for `datetime` → Use Argentina Time (ART, UTC-3) for display and storage; all latency measurements referenced to system clock assumed synced (NTP recommended).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST read instruments from the `MarketData` sheet `symbol` column (starting row 2 until blank) at startup.
- **FR-002**: System MUST subscribe to real-time (or near real-time) quote data (bid, bid_size, ask, ask_size, last, change, open, high, low, previous_close, turnover, volume, operations, datetime) for each symbol.
- **FR-003**: System MUST update each row's cells corresponding to received fields within 1 second p95 latency of tick arrival under normal load.
- **FR-004**: System MUST write a timestamp / datetime cell in the specified `datetime` column for each update using Argentina Time (ART, UTC-3) normalized via system clock (no UTC column required).
- **FR-005**: System MUST handle both base symbols and settlement variants (e.g., `GGAL` vs `GGAL-24hs`).
- **FR-006**: System MUST detect and begin updating newly added symbols within 10 seconds without restart.
- **FR-007**: System MUST set a boolean `Stale` column to TRUE when no update >5 seconds and revert to FALSE immediately upon next successful tick.
- **FR-008**: System MUST log (structured) each of: start, subscription attempt, reconnect, symbol error, stale detection, write failure.
- **FR-009**: System MUST not overwrite columns outside the defined header set (must ignore extraneous columns).
- **FR-010**: System MUST flush batched cell updates when (a) pending update count ≥50 OR (b) oldest pending age ≥200ms OR (c) a priority symbol update arrives—whichever occurs first—while still meeting latency targets.
- **FR-011**: System MUST gracefully retry transient network errors up to 3 times before escalating.
- **FR-012**: System MUST detect duplicate symbols and log a warning. Behavior after detection: update first.
- **FR-013 (Revised)**: System MUST maintain a per-symbol monotonically increasing sequence counter (starting at 1 on first tick) and detect gaps (difference >1). On a detected gap, attempt up to 3 resync/reconnect cycles; if unresolved, log an ERROR with gap metadata (symbol, expected, received) and continue without speculative fill.
- **FR-014**: System MUST allow configuration of refresh/re-scan interval for new symbols (default 10s).
- **FR-015**: System MUST provide a way to stop cleanly releasing any connection resources.
- **FR-016 (Augmented)**: System MUST support at least 200 symbols meeting latency targets AND limit in-memory tick retention to the most recent 100 ticks OR 5 minutes (whichever is smaller) per symbol; any queued update backlog after a burst MUST drain within 2 seconds.
- **FR-017**: System MUST validate presence of all mandatory header columns at startup and auto-create any that are missing (append in canonical order after existing columns); MUST NOT fail startup due solely to missing required columns (logs single structured info event listing created columns).
- **FR-018**: System MUST not block UI interactions with the Excel workbook during updates.
- **FR-019 (Elevated)**: System MUST provide a heartbeat/metrics summary row (or sheet) updated at least every 10 seconds containing: last_tick_time, ticks_received_total, ticks_written_total, dropped_ticks_total (by reason), reconnect_count, stale_count, last_flush_latency_ms (rolling), latency_p95_ms (rolling window).
- **FR-021**: System MUST ensure numeric formatting preserved (no locale misformat of decimals / thousands)
- **FR-022**: System MUST maintain ordering of symbol rows (not resort automatically).
- **FR-023**: System MUST allow safe manual workbook save while updates continue.
- **FR-024 (Clarified)**: System MUST degrade gracefully if turnover or operations fields are unavailable: leave the corresponding cells blank, emit a single INFO log per symbol per session (no retries), and continue processing other fields.
- **FR-025**: System MUST provide a configuration object/file for adjustable thresholds (stale seconds, batch size, latency targets) consistent with governance.

*New requirements appended to avoid renumbering prior references.*

**FR-026**: System MUST enforce stale eviction: instruments stale for >30 seconds are flagged as evicted (log event) and excluded from further stale scanning until a new tick arrives (which reactivates and clears eviction state).
**FR-027**: System MUST detect local system clock drift >100ms compared to incoming tick timestamps (ART) and emit a WARN log at most once per 10-minute window while drift persists.
**FR-028**: System MUST provide a replay test harness capable of ingesting recorded tick files (JSON lines) to deterministically reproduce Excel state (including sequences, stale transitions) for regression.
**FR-029**: System MUST normalize numeric value formatting using '.' as decimal separator, no thousands separator, and up to 4 decimal places (or fewer if source precision lower), ensuring locale settings do not alter output.
**FR-030**: System MUST emit structured JSON logs with schema elements: event_type (string), timestamp_utc, symbol (nullable), sequence (nullable), latency_ms (for tick/write events), reason (for drops/gaps), attempt (for retries). Missing required fields constitutes a logging defect.
**FR-031**: System MUST justify any new external dependency in the PR description citing capability gained or ≥30 LOC eliminated; merge blocked if justification absent.

*Ambiguity Markers retained intentionally for clarification prior to planning.*

### Deferred / Open Clarifications

- Turnover reset semantics across session boundaries: Deferred (Issue TBD) – treat turnover as cumulative for session; future FR may refine.

### Key Entities *(include if feature involves data)*

- **MarketInstrument**: Represents a tradable symbol (equity, bond, option, future, settlement variant). Attributes: symbol_code (string as appears in sheet), variant_type (spot, 24hs, other), last_quote (composite of quote fields), last_update_time, stale_flag, sequence/monotonic counter.
- **Quote**: Atomic snapshot of market data for a symbol at a moment. Fields: bid, bid_size, ask, ask_size, last, change, open, high, low, previous_close, turnover, volume, operations, event_time.
- **ExcelRowBinding**: Mapping between a MarketInstrument and a row index with column positions for each required field.
- **UpdateBatch**: Collection of pending cell updates grouped for a single flush to Excel to satisfy latency + batching rules.
- **MetricsSnapshot**: Aggregated counters (ticks_received_total, stale_count, reconnect_count, last_flush_latency).

---

## Review & Acceptance Checklist

### Gate: Automated checks run during main() execution


### Content Quality

- [ ] No implementation details (languages, frameworks, APIs)
- [ ] Focused on user value and business needs
- [ ] Written for non-technical stakeholders
- [ ] All mandatory sections completed

### Requirement Completeness

- [ ] No [NEEDS CLARIFICATION] markers remain
- [ ] Requirements are testable and unambiguous  
- [ ] Success criteria are measurable
- [ ] Scope is clearly bounded
- [ ] Dependencies and assumptions identified

---

## Execution Status

### Updated by main() during processing

- [ ] User description parsed
- [ ] Key concepts extracted
- [ ] Ambiguities marked
- [ ] User scenarios defined
- [ ] Requirements generated
- [ ] Entities identified
- [ ] Review checklist passed

---
