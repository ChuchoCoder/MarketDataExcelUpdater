# Implementation Plan: Near Real-Time Argentine Market → Excel Updater

**Branch**: `001-feature-description-build` | **Date**: 2025-09-25 | **Spec**: `specs/001-feature-description-build/spec.md`
**Input**: Feature specification at `specs/001-feature-description-build/spec.md`

## Execution Flow (/plan command scope)

```text
1. Load feature spec from Input path
   → If not found: ERROR "No feature spec at {path}"
2. Fill Technical Context (scan for NEEDS CLARIFICATION)
   → Detect Project Type from file system structure or context (web=frontend+backend, mobile=app+api)
   → Set Structure Decision based on project type
3. Fill the Constitution Check section based on the content of the constitution document.
4. Evaluate Constitution Check section below
   → If violations exist: Document in Complexity Tracking
   → If no justification possible: ERROR "Simplify approach first"
   → Update Progress Tracking: Initial Constitution Check
5. Execute Phase 0 → research.md
   → If NEEDS CLARIFICATION remain: ERROR "Resolve unknowns"
6. Execute Phase 1 → contracts, data-model.md, quickstart.md, agent-specific template file (e.g., `CLAUDE.md` for Claude Code, `.github/copilot-instructions.md` for GitHub Copilot, `GEMINI.md` for Gemini CLI, `QWEN.md` for Qwen Code or `AGENTS.md` for opencode).
7. Re-evaluate Constitution Check section
   → If new violations: Refactor design, return to Phase 1
   → Update Progress Tracking: Post-Design Constitution Check
8. Plan Phase 2 → Describe task generation approach (DO NOT create tasks.md)
9. STOP - Ready for /tasks command
```

**IMPORTANT**: The /plan command STOPS at step 7. Phases 2-4 are executed by other commands:

- Phase 2: /tasks command creates tasks.md
- Phase 3-4: Implementation execution (manual or via tools)

## Summary
Implement a low-latency .NET 9 console (or Windows tray) application that subscribes to Argentine market instruments via Primary.Net and continuously updates an open Excel workbook sheet `MarketData` with quote fields for ~200 symbols, meeting p95<1s (target 500ms per constitution) write latency and auto-managing staleness, duplicate symbols, missing columns, and dynamic symbol additions.

Core approach: Event-driven subscription adapter → in-memory symbol index → update coalescer & batcher → Excel late-bound COM writer (batched flush) + stale monitor + structured logging & metrics emitter.

## Technical Context

**Language/Version**: .NET 9 (C# 13)  
**Primary Dependencies**: Primary.Net (market data), Late-Bound COM Excel interop (dynamic dispatch), System.Text.Json for structured logs  
**Storage**: In-memory only (no persistence)  
**Testing**: xUnit + FluentAssertions + Moq + potential BenchmarkDotNet micro bench (latency hot path)  
**Target Platform**: Windows desktop (Excel installed)  
**Project Type**: Single project (console/service style) + test project  
**Performance Goals**: p95 tick→cell <500ms, p99 <1000ms; sustain 200 ticks/sec; batch flush ≤200ms cadence; backlog drain ≤2s post-burst  
**Constraints**: ≤100MB RSS, in-memory tick retention max 100 ticks or 5 minutes per symbol (whichever smaller); stale eviction at 30s; no external DB; minimal dependencies; O(1) per tick path  
**Scale/Scope**: ~200 symbols initial; extensible to 500 with batching tweaks  

Rationale: Single-process architecture satisfies simplicity & latency; no message bus required at current scale per constitution Principle II.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Principle Mapping:

- I (Data Integrity): Plan includes canonical symbol map, explicit stale flag logic, structured logging of anomalies.
- II (Low-Latency Simplicity): Single in-proc pipeline, batching thresholds defined, no premature microservices.
- III (Test-First Reliability): TDD sequence: model + adapter contracts → stale logic tests → batch flush tests → integration replay harness.
- IV (Observability): Metrics & log events enumerated (see Design). Heartbeat row planned.
- V (Minimal Dependencies): Only Primary.Net + Late-Bound COM Excel interop; no external Excel library packages required; justify if adding more (e.g., metrics lib).

No violations at this stage. Proceed.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/plan command output)
├── research.md          # Phase 0 output (/plan command)
├── data-model.md        # Phase 1 output (/plan command)
├── quickstart.md        # Phase 1 output (/plan command)
├── contracts/           # Phase 1 output (/plan command)
└── tasks.md             # Phase 2 output (/tasks command - NOT created by /plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->
```text
src/
   Core/               # Domain models, value objects
   MarketData/         # Primary.Net adapter + subscription management
   Excel/              # Excel abstraction (IExcelWriter, batching impl)
   Pipeline/           # Orchestration: tick dispatch, batching, stale monitor
   Metrics/            # Metrics & heartbeat row logic
   Config/             # Configuration loading & validation
   Logging/            # Structured logging helpers
   Program.cs          # Composition root

tests/
   Unit/
      Core/
      Excel/
      Pipeline/
   Contract/
      MarketDataAdapterTests.cs
   Integration/
      EndToEndWorkbookFlowTests.cs
   Replay/
      Samples/ (recorded tick JSON)
```

**Structure Decision**: Single-project layout with vertical feature folders for clarity and low coupling; one accompanying test project mirroring folder topology for discoverability.

## Phase 0: Outline & Research

1. **Extract unknowns from Technical Context** above:
   - For each NEEDS CLARIFICATION → research task
   - For each dependency → best practices task
   - For each integration → patterns task

2. **Generate and dispatch research agents**:

```text
For each unknown in Technical Context:
   Task: "Research {unknown} for {feature context}"
For each technology choice:
   Task: "Find best practices for {tech} in {domain}"
```

1. **Consolidate findings** in `research.md` using format:
   - Decision: [what was chosen]
   - Rationale: [why chosen]
   - Alternatives considered: [what else evaluated]

**Output**: research.md with all NEEDS CLARIFICATION resolved

Planned Research Topics:

1. Excel write strategy: Late-Bound COM interop implementation & thread safety.
2. Primary.Net subscription API surface: async pattern, reconnect semantics.
3. Batching algorithm parameters: dynamic vs fixed threshold (latency impact).
4. Metrics emission: latency histogram, dropped tick classification, reconnect counts vs lightweight library.
5. Heartbeat row cadence & minimal recalculation cost.
6. Time synchronization & clock drift monitoring (.NET TimeProvider?).
7. Sequence gap detection & minimal resync strategy (3-attempt policy).
8. Tick retention structures (ring buffer vs deque) under memory constraints.
9. Replay harness design for deterministic regression.

Research Acceptance Criteria:

- Each topic yields Decision / Rationale / Alternatives.
- Confirms no additional mandatory dependencies.
- Establishes test harness strategy for replay ticks.

## Phase 1: Design & Contracts

Prerequisite: research.md complete

1. **Extract entities from feature spec** → `data-model.md` (include sequence counter, retention policy & eviction flags):
   - Entity name, fields, relationships
   - Validation rules from requirements
   - State transitions if applicable

2. **Generate API contracts** (ADAPTED): No external HTTP API; instead define internal interfaces (include sequence & gap reporting events if needed):
   - IMarketDataSubscriber (Subscribe(symbols), Events: OnQuote)
   - IExcelWriter (QueueUpdate(symbol, field, value), FlushAsync())
   - IStaleMonitor (TrackUpdate(symbol, timestamp), GetStaleSet())
   - IBatchPolicy (ShouldFlush(now, pendingCount))
   Place interface specs in `/contracts/` as markdown describing method signatures (since no network contract).

3. **Generate contract tests** from internal contracts (include gap detection & retention boundary tests once basic failing tests exist):
   - One test per interface verifying default mock expectations & behavior assumptions (e.g., batch policy thresholds).
   - Initial tests fail referencing NotImplemented mocks to enforce TDD.

4. **Extract test scenarios** from user stories:
   - Each story → integration test scenario
   - Quickstart test = story validation steps

5. **Update agent file incrementally** (O(1) operation) – deferred until first code artifacts exist to avoid empty context noise.
6. Define structured log schema (event_type, symbol?, sequence?, latency_ms?, reason?, attempt?) and validate against constitution.

**Output**: data-model.md, /contracts/*, failing tests, quickstart.md, agent-specific file

## Phase 2: Task Planning Approach

This section describes what the /tasks command will do - DO NOT execute during /plan

**Task Generation Strategy**:

- Load `.specify/templates/tasks-template.md` as base
- Generate tasks from Phase 1 design docs (contracts, data model, quickstart)
- Each contract → contract test task [P]
- Each entity → model creation task [P]
- Each user story → integration test task
- Implementation tasks to make tests pass

**Ordering Strategy**:

- TDD order: Tests before implementation
- Dependency order: Models before services before UI
- Mark [P] for parallel execution (independent files)

**Estimated Output**: 25-30 numbered, ordered tasks in tasks.md

**IMPORTANT**: This phase is executed by the /tasks command, NOT by /plan

## Phase 3+: Future Implementation

These phases are beyond the scope of the /plan command

**Phase 3**: Task execution (/tasks command creates tasks.md)  
**Phase 4**: Implementation (execute tasks.md following constitutional principles)  
**Phase 5**: Validation (run tests, execute quickstart.md, performance validation)

## Complexity Tracking

Fill ONLY if Constitution Check has violations that must be justified

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |


## Progress Tracking

This checklist is updated during execution flow

**Phase Status**:

- [ ] Phase 0: Research complete (/plan command)
- [ ] Phase 1: Design complete (/plan command)
- [ ] Phase 2: Task planning complete (/plan command - describe approach only)
- [ ] Phase 3: Tasks generated (/tasks command)
- [ ] Phase 4: Implementation complete
- [ ] Phase 5: Validation passed

**Gate Status**:

- [ ] Initial Constitution Check: PASS
- [ ] Post-Design Constitution Check: PASS
- [ ] All NEEDS CLARIFICATION resolved
- [ ] Complexity deviations documented

---
*Based on Constitution v1.0.0 - See `/memory/constitution.md`*
