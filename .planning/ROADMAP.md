# Roadmap: DocAgentFramework

## Overview

Six phases build the compiler-grade code documentation memory system from the ground up. Core domain contracts are locked first because every downstream artifact depends on them. Ingestion produces the snapshot that indexing consumes. The query facade isolates business logic before MCP tools are wired on top. Security and serving ship together as one boundary. Analysis and hosting are additive finishes once the pipeline is verified end-to-end.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Core Domain** - Lock SymbolId spec, snapshot schema, and all interface contracts (completed 2026-02-26)
- [x] **Phase 2: Ingestion Pipeline** - Walk Roslyn symbols, parse XML docs, produce deterministic snapshots (completed 2026-02-26)
- [x] **Phase 3: BM25 Search Index** - Replace stub index with Lucene.Net BM25 and CamelCase tokenization (completed 2026-02-26)
- [x] **Phase 4: Query Facade** - Wire IKnowledgeQueryService over index and snapshot store (completed 2026-02-26)
- [x] **Phase 5: MCP Server + Security** - Expose all five MCP tools with path allowlist and audit logging (completed 2026-02-27)
- [ ] **Phase 7: Runtime Integration Wiring** - DI registration, ArtifactsDir config, GetReferencesAsync impl, E2E pipeline fix
- [ ] **Phase 6: Analysis + Hosting** - Roslyn analyzers, doc coverage policy, Aspire wiring, OpenTelemetry

## Phase Details

### Phase 1: Core Domain
**Goal**: All domain contracts are locked and every downstream component has a stable foundation to build against
**Depends on**: Nothing (first phase)
**Requirements**: CORE-01, CORE-02, CORE-03
**Success Criteria** (what must be TRUE):
  1. `SymbolId` value equality holds across rename scenarios via `PreviousIds` tracking, verified by a golden-file test
  2. `SymbolGraphSnapshot` roundtrips through deterministic serialization with a stable content hash
  3. All six domain interfaces (`IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `ISearchIndex`, `IKnowledgeQueryService`, plus `IVectorIndex` stub) compile with zero warnings-as-errors
  4. `dotnet test` passes with no stub-only tests failing
**Plans**: 3 plans

Plans:
- [ ] 01-01-PLAN.md — Expand domain types (SymbolId, enums, records) + CORE-01 tests
- [ ] 01-02-PLAN.md — MessagePack serialization, SerializationFormat enum, CORE-02 tests
- [ ] 01-03-PLAN.md — Interface contracts (IAsyncEnumerable, IVectorIndex stub, extensions)

### Phase 2: Ingestion Pipeline
**Goal**: A real .NET project can be ingested and produces a byte-identical SymbolGraphSnapshot across runs
**Depends on**: Phase 1
**Requirements**: INGS-01, INGS-02, INGS-03, INGS-04, INGS-05
**Success Criteria** (what must be TRUE):
  1. Running ingestion twice on the same project produces byte-identical `SymbolGraphSnapshot` artifacts (determinism test passes)
  2. XML doc elements (summary, param, returns, remarks, exceptions) are bound to their correct `SymbolNode` by XML doc ID round-trip
  3. Edge cases produce tracked failures rather than silent drops: generics, partial types, overloads, operators, and `inheritdoc` expansion are each covered by a test
  4. Versioned snapshots are readable from and written to `artifacts/snapshots/` by `SnapshotStore`
  5. Roslyn `Compilation` objects are released after snapshot build (no unbounded memory retention)
**Plans**: 5 plans

Plans:
- [ ] 02-01-PLAN.md — LocalProjectSource + MSBuildWorkspace package setup (Wave 1)
- [ ] 02-02-PLAN.md — XmlDocParser + InheritDocResolver full implementation (Wave 1)
- [ ] 02-03-PLAN.md — RoslynSymbolGraphBuilder + SymbolSorter (Wave 2)
- [ ] 02-04-PLAN.md — SnapshotStore with manifest.json (Wave 2)
- [ ] 02-05-PLAN.md — Determinism test suite + full regression (Wave 3)

### Phase 3: BM25 Search Index
**Goal**: Symbol and documentation text is searchable with BM25 ranking and CamelCase-aware tokenization
**Depends on**: Phase 2
**Requirements**: INDX-01, INDX-02, INDX-03
**Success Criteria** (what must be TRUE):
  1. Searching for `GetSymbol` returns results ranked above unrelated symbols containing partial token matches
  2. CamelCase query `getRef` resolves to symbols containing `GetReferences` (case-insensitive token split)
  3. The index is persisted alongside its snapshot and reloaded without re-ingesting the source project
  4. `InMemorySearchIndex` is no longer used in any non-test code path
**Plans**: 2 plans

Plans:
- [ ] 03-01-PLAN.md — BM25SearchIndex + CamelCaseAnalyzer implementation and unit tests (INDX-01, INDX-02)
- [ ] 03-02-PLAN.md — Index persistence with FSDirectory and freshness check (INDX-03)

### Phase 4: Query Facade
**Goal**: All query operations are testable through IKnowledgeQueryService without a running MCP server
**Depends on**: Phase 3
**Requirements**: QURY-01, QURY-02, QURY-03, QURY-04
**Success Criteria** (what must be TRUE):
  1. `SearchAsync` returns ranked `SymbolNode` results from the BM25 index via the facade
  2. `GetSymbolAsync` returns full symbol detail by `SymbolId` from the snapshot store
  3. `DiffAsync` returns a structural diff (added, removed, modified symbols) between two snapshot versions
  4. All three operations are tested without starting the MCP server process
**Plans**: TBD

Plans:
- [ ] 04-01: KnowledgeQueryService implementation wired to ISearchIndex + SnapshotStore
- [ ] 04-02: SearchAsync and GetSymbolAsync
- [ ] 04-03: DiffAsync basic structural diff

### Phase 5: MCP Server + Security
**Goal**: Agents can query the symbol graph through all five MCP tools over stdio with security boundaries enforced
**Depends on**: Phase 4
**Requirements**: MCPS-01, MCPS-02, MCPS-03, MCPS-04, MCPS-05, MCPS-06, SECR-01, SECR-02, SECR-03
**Success Criteria** (what must be TRUE):
  1. All five tools (`search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`) return valid responses via `mcp inspect` on a real codebase snapshot
  2. Raw stdout byte capture contains only valid JSON-RPC frames — no log lines, exception traces, or other contamination
  3. A path outside the configured allowlist returns a structured error, not an exception or filesystem path in the response
  4. Every tool call is written to the audit log with input and output before the response is returned
  5. A doc comment containing prompt injection text is returned as structured data, not executed as an instruction
**Plans**: TBD

Plans:
- [ ] 05-01: DocTools.cs handlers for all five MCP tools (ModelContextProtocol 1.0.0 API)
- [ ] 05-02: PathAllowlist — default-deny, configured allowed directories
- [ ] 05-03: AuditLogger — log every tool call input/output
- [ ] 05-04: Stderr-only logging configuration and stdout contamination integration test
- [ ] 05-05: Input validation DTOs and prompt injection defense

### Phase 7: Runtime Integration Wiring
**Goal**: The MCP server runs end-to-end at runtime — DI container resolves all services, artifact paths are configurable, and all five tools return real results
**Depends on**: Phase 5
**Requirements**: MCPS-03 (GetReferencesAsync), plus integration fixes for QURY-01, INDX-01, INDX-03, INGS-04, MCPS-01–05
**Gap Closure:** Closes integration and flow gaps from v1.0 audit
**Success Criteria** (what must be TRUE):
  1. `IKnowledgeQueryService`, `ISearchIndex` (BM25SearchIndex), and `SnapshotStore` are registered in Program.cs DI container
  2. `DocAgentServerOptions.ArtifactsDir` is configurable and flows to SnapshotStore and BM25SearchIndex
  3. `GetReferencesAsync` returns real edges from the snapshot graph (not empty)
  4. E2E integration test: .sln → discover → parse → snapshot → index → search → MCP response succeeds
**Plans**: TBD

Plans:
- [ ] 07-01: DI registrations + ArtifactsDir config property
- [ ] 07-02: GetReferencesAsync real implementation
- [ ] 07-03: E2E integration test + tech debt cleanup (InMemorySearchIndex removal)

### Phase 6: Analysis + Hosting
**Goal**: Roslyn analyzers enforce doc parity in CI and the server runs under Aspire with observable telemetry
**Depends on**: Phase 7
**Requirements**: ANLY-01, ANLY-02, ANLY-03, HOST-01, HOST-02
**Success Criteria** (what must be TRUE):
  1. Adding a public method without a `<summary>` doc comment triggers the doc parity analyzer as a build warning/error
  2. A semantic change (signature change) without a corresponding doc or test update triggers the suspicious edit analyzer
  3. Doc coverage drops below the configured threshold causes the policy build gate to fail
  4. `dotnet run --project src/DocAgent.AppHost` starts the MCP server and surfaces tool call spans in the Aspire dashboard
  5. OpenTelemetry traces show per-tool-call spans with input/output metadata
**Plans**: TBD

Plans:
- [ ] 06-01: Roslyn DiagnosticAnalyzer — public API changes without doc updates (ANLY-01)
- [ ] 06-02: Roslyn DiagnosticAnalyzer — suspicious edits without doc/test updates (ANLY-02)
- [ ] 06-03: Doc coverage policy enforcement gate (ANLY-03)
- [ ] 06-04: DocAgent.AppHost DI extension methods and Aspire wiring (HOST-01)
- [ ] 06-05: OpenTelemetry tool call observation (HOST-02)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 7 → 6

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Core Domain | 3/3 | Complete   | 2026-02-26 |
| 2. Ingestion Pipeline | 5/5 | Complete   | 2026-02-26 |
| 3. BM25 Search Index | 2/2 | Complete   | 2026-02-26 |
| 4. Query Facade | 2/2 | Complete   | 2026-02-26 |
| 5. MCP Server + Security | 3/3 | Complete   | 2026-02-27 |
| 7. Runtime Integration Wiring | 2/3 | In Progress|  |
| 6. Analysis + Hosting | 0/5 | Not started | - |
