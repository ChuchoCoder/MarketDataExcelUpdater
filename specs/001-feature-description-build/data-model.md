# Data Model

Date: 2025-09-25

## Entities

### MarketInstrument

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| Symbol | string | Canonical symbol or variant (e.g., GGAL, GGAL-24hs) | Non-empty, unique first occurrence |
| VariantType | enum {Spot, Settlement24h, Other} | Differentiates settlement variants | Derived from naming convention |
| LastQuote | Quote | Most recent full quote snapshot | Updated atomically |
| LastUpdateTime | DateTime (ART) | Time of last applied quote | Monotonic non-decreasing |
| Stale | bool | TRUE if >5s since last update | Set/reset by StaleMonitor |

### Quote

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| Bid | decimal? | Best bid | >=0 or null |
| BidSize | decimal? | Bid size | >=0 or null |
| Ask | decimal? | Best ask | >=0 or null |
| AskSize | decimal? | Ask size | >=0 or null |
| Last | decimal? | Last traded price | >=0 or null |
| Change | decimal? | Change vs previous_close | nullable |
| Open | decimal? | Session open | nullable |
| High | decimal? | Session high | nullable |
| Low | decimal? | Session low | nullable |
| PreviousClose | decimal? | Prior session close | nullable |
| Turnover | decimal? | Monetary value traded | nullable |
| Volume | long? | Total volume | >=0 |
| Operations | long? | Trade count | >=0 |
| EventTime | DateTime (ART) | Provider event time | Required |

### ExcelRowBinding

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| Symbol | string | Key to MarketInstrument | Must exist in instrument map |
| RowIndex | int | Excel row number | >1 |
| ColumnMap | Dictionary<string,int> | Maps field names to column indices | Must cover required headers |

### UpdateBatch

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| Updates | List&lt;CellUpdate&gt; | Pending cell writes | <= configurable max before forced flush |
| OldestEnqueued | DateTime | Timestamp oldest update queued | Used for timeout flush |

### CellUpdate

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| Row | int | Target row | >=2 |
| Column | int | Target column | >=1 |
| Value | object | Formatted value | Formatting preserved |

### MetricsSnapshot

| Field | Type | Description | Constraints |
|-------|------|-------------|-------------|
| TicksReceived | long | Total ticks accepted | >=0 |
| TicksWritten | long | Total ticks written | >=0 |
| DroppedTicks | long | Ticks discarded (reason) | >=0 |
| StaleCount | int | Current stale instruments | >=0 |
| ReconnectCount | int | Connection reconnects | >=0 |
| LastFlushLatencyMs | double | Last batch flush latency | >=0 |

## Relationships

- MarketInstrument 1 — 1 Quote (embedded current view)
- MarketInstrument 1 — 1 ExcelRowBinding (by symbol)
- UpdateBatch aggregates CellUpdate entries
- MetricsSnapshot independent (periodic sampling)

## Invariants

- If Stale=true then (Now - LastUpdateTime) > 5s.
- Bid <= Ask when both non-null.
- LastUpdateTime monotonic per symbol.
- All required columns present or auto-created before first write.

## Derived Values

- Change may be computed from Last - PreviousClose if not provided.
- Stale flag derived from (Now - LastUpdateTime).

## State Transitions (Instrument)

| From | Event | To | Action |
|------|-------|----|--------|
| Fresh | 5s silence | Stale | Set Stale=true, log stale event |
| Stale | Quote received | Fresh | Stale=false, update quote |

## Validation Rules

- Reject ticks with negative numeric fields.
- Ignore ticks older than current LastUpdateTime (out-of-order safeguard).
- Auto-create columns before first batch flush if missing.

## Open / Deferred

- Turnover reset semantics (tbd) – assume cumulative for now.
