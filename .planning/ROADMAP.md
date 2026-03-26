# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- ✅ **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-18 (shipped 2026-03-02)
- ✅ **v1.3 Housekeeping** — Phases 19-22 (shipped 2026-03-04)
- ✅ **v1.5 Robustness** — Phases 23-27 (shipped 2026-03-08)
- [ ] **v2.0 TypeScript Language Support** — Phases 28-34 (in progress)

## Phases

<details>
<summary>v1.0 MVP (Phases 1-8) -- SHIPPED 2026-02-28</summary>

- [x] Phase 1: Core Domain (3/3 plans) -- completed 2026-02-26
- [x] Phase 2: Ingestion Pipeline (5/5 plans) -- completed 2026-02-26
- [x] Phase 3: BM25 Search Index (2/2 plans) -- completed 2026-02-26
- [x] Phase 4: Query Facade (2/2 plans) -- completed 2026-02-26
- [x] Phase 5: MCP Server + Security (3/3 plans) -- completed 2026-02-27
- [x] Phase 7: Runtime Integration Wiring (3/3 plans) -- completed 2026-02-27
- [x] Phase 6: Analysis + Hosting (4/4 plans) -- completed 2026-02-28
- [x] Phase 8: Ingestion Runtime Trigger (2/2 plans) -- completed 2026-02-28

Full details: milestones/v1.0-ROADMAP.md

</details>

<details>
<summary>v1.1 Semantic Diff & Change Intelligence (Phases 9-12) -- SHIPPED 2026-03-01</summary>

- [x] Phase 9: Semantic Diff Engine (3/3 plans) -- completed 2026-02-28
- [x] Phase 10: Incremental Ingestion (3/3 plans) -- completed 2026-02-28
- [x] Phase 11: Change Intelligence & Review (2/2 plans) -- completed 2026-02-28
- [x] Phase 12: ChangeTools Security Gate (1/1 plan) -- completed 2026-03-01

Full details: milestones/v1.1-ROADMAP.md

</details>

<details>
<summary>v1.2 Multi-Project & Solution-Level Graphs (Phases 13-18) -- SHIPPED 2026-03-02</summary>

- [x] Phase 13: Core Domain Extensions (2/2 plans) -- completed 2026-03-01
- [x] Phase 14: Solution Ingestion Pipeline (2/2 plans) -- completed 2026-03-01
- [x] Phase 14.1: Solution Graph Enrichment (2/2 plans) -- completed 2026-03-01
- [x] Phase 15: Project-Aware Indexing & Query (2/2 plans) -- completed 2026-03-01
- [x] Phase 16: Solution MCP Tools (2/2 plans) -- completed 2026-03-02
- [x] Phase 18: Fix diff_snapshots Tool Name Collision (1/1 plan) -- completed 2026-03-02

Full details: milestones/v1.2-ROADMAP.md

Known gap: Phase 17 (Incremental Solution Re-ingestion / INGEST-05) deferred to v1.3

</details>

<details>
<summary>v1.3 Housekeeping (Phases 19-22) -- SHIPPED 2026-03-04</summary>

- [x] Phase 19: Incremental Solution Re-ingestion (4/4 plans) -- completed 2026-03-02
- [x] Phase 20: MSBuild Performance Benchmarks (2/2 plans) -- completed 2026-03-03
- [x] Phase 21: Code and Audit Cleanup (1/1 plan) -- completed 2026-03-03
- [x] Phase 22: Documentation Refresh (1/1 plan) -- completed 2026-03-04

Full details: milestones/v1.3-ROADMAP.md

</details>

<details>
<summary>v1.5 Robustness (Phases 23-27) -- SHIPPED 2026-03-08</summary>

- [x] Phase 23: Dependency Foundation (1/1 plan) -- completed 2026-03-06
- [x] Phase 24: Query Performance (1/1 plan) -- completed 2026-03-08
- [x] Phase 25: Server Infrastructure (2/2 plans) -- completed 2026-03-08
- [x] Phase 26: API Extensions (2/2 plans) -- completed 2026-03-08
- [x] Phase 27: Documentation Refresh (1/1 plan) -- completed 2026-03-08

Full details: milestones/v1.5-ROADMAP.md

</details>

### v2.0 TypeScript Language Support (In Progress)

**Milestone Goal:** Make TypeScript codebases queryable through the same 14 MCP tools via a Node.js sidecar using the TypeScript Compiler API.

- [ ] **Phase 28: Sidecar Scaffold and IPC Protocol** - Node.js project, NDJSON protocol, C# process spawning, Aspire orchestration
- [x] **Phase 29: Core Symbol Extraction** - TypeScript Compiler API walker producing SymbolNode/SymbolEdge graphs with stable IDs (completed 2026-03-24)
- [x] **Phase 30: MCP Integration and Incremental Ingestion** - ingest_typescript tool, incremental file hashing, BM25 tokenization (completed 2026-03-25)
- [x] **Phase 31: Verification and Hardening** - Determinism, cross-tool validation, security, performance profiling (completed 2026-03-25)

## Phase Details

### Phase 28: Sidecar Scaffold and IPC Protocol
**Goal**: A working Node.js sidecar process can receive a tsconfig.json path over NDJSON stdin and return a stub SymbolGraphSnapshot response, orchestrated by Aspire with startup validation
**Depends on**: Phase 27 (v1.5 complete)
**Requirements**: SIDE-01, SIDE-02, SIDE-03, SIDE-04
**Success Criteria** (what must be TRUE):
  1. Running `dotnet run --project src/DocAgent.AppHost` starts the Node.js sidecar as a managed Aspire resource, and startup fails with a clear error if Node.js is not installed
  2. The C# `TypeScriptIngestionService` can spawn the sidecar process, send an NDJSON request with a tsconfig.json path on stdin, and receive a valid (stub) SymbolGraphSnapshot-shaped JSON response on stdout
  3. The sidecar project has package.json, esbuild bundling to a single dist/index.js, and vitest test setup with at least one passing test
  4. All sidecar logging goes to stderr; no stdout pollution breaks NDJSON deserialization
**Plans**: 2 plans

Plans:
- [ ] 28-01-PLAN.md — Node.js sidecar scaffold, NDJSON IPC protocol, stub extractor with vitest tests
- [ ] 28-02-PLAN.md — C# TypeScriptIngestionService, NodeAvailabilityValidator, DI registration

### Phase 29: Core Symbol Extraction
**Goal**: The TypeScript sidecar extracts all declaration types, relationships, documentation, and source spans from a real TypeScript project into a complete, deterministic SymbolGraphSnapshot
**Depends on**: Phase 28
**Requirements**: EXTR-01, EXTR-02, EXTR-03, EXTR-04, EXTR-05, EXTR-06, EXTR-07, EXTR-08
**Success Criteria** (what must be TRUE):
  1. Given a TypeScript project with classes, interfaces, functions, enums, and type aliases, the sidecar produces a SymbolGraphSnapshot containing a SymbolNode for every declaration with correct SymbolKind, accessibility, and source span
  2. Every extracted symbol has a stable, deterministic SymbolId that does not change across repeated ingestions of the same source and does not collide with C# SymbolIds
  3. TypeScript modules map to SymbolKind.Namespace nodes using relative file paths, with Contains edges linking declarations to their parent module
  4. Inheritance (extends) and implementation (implements) relationships appear as SymbolEdge entries with correct edge kinds
  5. JSDoc/TSDoc comments are extracted into DocComment fields (summary, param, returns, example, throws, see, remarks) and source files from node_modules are excluded from the snapshot
**Plans**: 3 plans

Plans:
- [x] 29-01-PLAN.md — SymbolId Design, Source File Walker, and Golden-file Infrastructure
- [x] 29-02-PLAN.md — Full Symbol & Doc Extraction
- [ ] 29-03-PLAN.md — Gap closure: expand fixture coverage for enums, type aliases, constructors, fields

### Phase 30: MCP Integration and Incremental Ingestion
**Goal**: Users can call `ingest_typescript` via MCP to ingest a TypeScript project, query it with all 14 existing tools, and benefit from incremental re-ingestion on subsequent calls
**Depends on**: Phase 29
**Requirements**: MCPI-01, MCPI-02, MCPI-03, MCPI-04
**Success Criteria** (what must be TRUE):
  1. Calling the `ingest_typescript` MCP tool with a tsconfig.json path ingests the TypeScript project and returns a snapshot hash, with PathAllowlist security enforced on the input path
  2. After ingestion, all 14 existing MCP tools (search_symbols, get_symbol, get_references, find_implementations, get_doc_coverage, diff_snapshots, explain_project, review_changes, find_breaking_changes, explain_change, ingest_project, ingest_solution, explain_solution, diff_solution_snapshots) produce correct results when querying the TypeScript snapshot
  3. Re-ingesting the same TypeScript project after modifying one file only re-parses the changed file (SHA-256 file hashing) and produces an updated snapshot
  4. BM25 search correctly tokenizes and matches camelCase TypeScript symbol names (e.g., searching "create" finds "createServer")
**Plans**: 3 plans

Plans:
- [x] 30-01-PLAN.md — Ingest TypeScript MCP Tool and Incremental Ingestion
- [x] 30-02-PLAN.md — Search Refinement and E2E Verification
- [x] 30-03-PLAN.md — Gap closure: TypeScript tool verification tests and camelCase search integration tests

### Phase 31: Verification and Hardening
**Goal**: The TypeScript ingestion pipeline is proven deterministic, secure, and performant through comprehensive validation against real-world projects
**Depends on**: Phase 30
**Requirements**: VERF-01, VERF-02, VERF-03, VERF-04
**Success Criteria** (what must be TRUE):
  1. Golden-file tests confirm that ingesting the same TypeScript project twice produces byte-identical SymbolGraphSnapshot output
  2. A cross-tool validation suite exercises all 14 MCP tools against TypeScript snapshots and verifies correct results (search hits, symbol details, reference traversal, diff output, change review)
  3. Security validation confirms PathAllowlist enforcement on ingest_typescript, no absolute paths leak in SymbolNode.Span fields, and audit logging captures TypeScript ingestion events
  4. Performance profiling on a 500+ file TypeScript project completes within established baseline thresholds and identifies no IPC serialization bottlenecks
**Plans**: 4 plans

Plans:
- [x] 31-01-PLAN.md — Performance and Large-Scale Validation
- [x] 31-02-PLAN.md — Robustness and Security Hardening
- [ ] 31-03-PLAN.md — Gap closure: Fix absolute path leak in sidecar spans and remove debug logging
- [ ] 31-04-PLAN.md — Gap closure: TypeScript audit logging and Architecture.md documentation

### Phase 32: JSON Contract Alignment (TS <-> C# Deserialization)
**Goal:** Fix all JSON deserialization mismatches between TypeScript sidecar output and C# domain types so the real sidecar->C# pipeline produces correct SymbolGraphSnapshots
**Depends on**: Phase 31
**Requirements**: SIDE-03, EXTR-04, EXTR-06, MCPI-01, MCPI-02
**Gap Closure:** Closes gaps from v2.0 audit — JSON property names, enum ordinals, E2E integration
**Success Criteria** (what must be TRUE):
  1. SymbolEdge property names align: TS `sourceId`/`targetId` correctly deserialize to C# `From`/`To`
  2. SymbolNode doc property aligns: TS `docComment` correctly deserializes to C# `Docs`
  3. SymbolEdgeKind enum ordinals match between TS and C# (e.g., `Inherits` maps to the same integer on both sides)
  4. An E2E integration test exercises real sidecar -> JSON -> C# deserialization (no PipelineOverride) and produces a valid, queryable snapshot
**Plans**: 2 plans

Plans:
- [ ] 32-01-PLAN.md — TS string enum conversion + C# deserialization alignment (JsonPropertyName, DocCommentConverter, JsonStringEnumConverter)
- [ ] 32-02-PLAN.md — Golden file deserialization tests + real sidecar E2E integration tests

### Phase 33: Aspire Sidecar Integration
**Goal:** Register the Node.js sidecar as a managed Aspire resource in AppHost so `dotnet run --project src/DocAgent.AppHost` starts and orchestrates the sidecar
**Depends on**: Phase 32
**Requirements**: SIDE-04
**Gap Closure:** Closes gaps from v2.0 audit — Aspire orchestration flow
**Success Criteria** (what must be TRUE):
  1. `DocAgent.AppHost/Program.cs` registers the Node.js sidecar via `AddNodeApp()` or equivalent Aspire API
  2. `NodeAvailabilityValidator` is Aspire-aware and integrates with resource health checks
  3. Running `dotnet run --project src/DocAgent.AppHost` starts the sidecar and fails with a clear error if Node.js is unavailable
**Plans**: 1 plan

Plans:
- [ ] 33-01-PLAN.md — AppHost sidecar registration, NodeAvailabilityHealthCheck, DOCAGENT_SIDECAR_DIR env var wiring

### Phase 34: Traceability and Verification Cleanup
**Goal:** Update all stale requirement checkboxes, create missing verification artifacts, and fill Nyquist validation gaps
**Depends on**: Phase 32, Phase 33
**Requirements**: SIDE-01, SIDE-02, MCPI-02, MCPI-04
**Gap Closure:** Closes tech debt and Nyquist gaps from v2.0 audit
**Success Criteria** (what must be TRUE):
  1. SIDE-01, SIDE-02 checkboxes in REQUIREMENTS.md reflect verified satisfaction
  2. MCPI-02, MCPI-04 checkboxes updated after Phase 32 fixes
  3. Phase 28 has a retroactive VERIFICATION.md
  4. Phases 29, 30, 31 have VALIDATION.md (Nyquist compliance)
**Plans**: 2 plans

Plans:
- [ ] 34-01-PLAN.md — Phase 28 retroactive VERIFICATION.md + REQUIREMENTS.md checkbox updates
- [ ] 34-02-PLAN.md — Retroactive VALIDATION.md for phases 29, 30, 31 (Nyquist compliance)

## Progress

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Core Domain | v1.0 | 3/3 | Complete | 2026-02-26 |
| 2. Ingestion Pipeline | v1.0 | 5/5 | Complete | 2026-02-26 |
| 3. BM25 Search Index | v1.0 | 2/2 | Complete | 2026-02-26 |
| 4. Query Facade | v1.0 | 2/2 | Complete | 2026-02-26 |
| 5. MCP Server + Security | v1.0 | 3/3 | Complete | 2026-02-27 |
| 7. Runtime Integration Wiring | v1.0 | 3/3 | Complete | 2026-02-27 |
| 6. Analysis + Hosting | v1.0 | 4/4 | Complete | 2026-02-28 |
| 8. Ingestion Runtime Trigger | v1.0 | 2/2 | Complete | 2026-02-28 |
| 9. Semantic Diff Engine | v1.1 | 3/3 | Complete | 2026-02-28 |
| 10. Incremental Ingestion | v1.1 | 3/3 | Complete | 2026-02-28 |
| 11. Change Intelligence & Review | v1.1 | 2/2 | Complete | 2026-02-28 |
| 12. ChangeTools Security Gate | v1.1 | 1/1 | Complete | 2026-03-01 |
| 13. Core Domain Extensions | v1.2 | 2/2 | Complete | 2026-03-01 |
| 14. Solution Ingestion Pipeline | v1.2 | 2/2 | Complete | 2026-03-01 |
| 14.1 Solution Graph Enrichment | v1.2 | 2/2 | Complete | 2026-03-01 |
| 15. Project-Aware Indexing & Query | v1.2 | 2/2 | Complete | 2026-03-01 |
| 16. Solution MCP Tools | v1.2 | 2/2 | Complete | 2026-03-02 |
| 18. Fix diff_snapshots Collision | v1.2 | 1/1 | Complete | 2026-03-02 |
| 19. Incremental Solution Re-ingestion | v1.3 | 4/4 | Complete | 2026-03-02 |
| 20. MSBuild Performance Benchmarks | v1.3 | 2/2 | Complete | 2026-03-03 |
| 21. Code and Audit Cleanup | v1.3 | 1/1 | Complete | 2026-03-03 |
| 22. Documentation Refresh | v1.3 | 1/1 | Complete | 2026-03-04 |
| 23. Dependency Foundation | v1.5 | 1/1 | Complete | 2026-03-06 |
| 24. Query Performance | v1.5 | 1/1 | Complete | 2026-03-08 |
| 25. Server Infrastructure | v1.5 | 2/2 | Complete | 2026-03-08 |
| 26. API Extensions | v1.5 | 2/2 | Complete | 2026-03-08 |
| 27. Documentation Refresh | v1.5 | 1/1 | Complete | 2026-03-08 |
| 28. Sidecar Scaffold and IPC Protocol | v2.0 | 2/2 | Complete | 2026-03-08 |
| 29. Core Symbol Extraction | v2.0 | 3/3 | Complete | 2026-03-24 |
| 30. MCP Integration and Incremental Ingestion | v2.0 | 3/3 | Complete | 2026-03-25 |
| 31. Verification and Hardening | v2.0 | 4/4 | Complete | 2026-03-25 |
| 32. JSON Contract Alignment | v2.0 | 2/2 | Complete | 2026-03-26 |
| 33. Aspire Sidecar Integration | v2.0 | 1/1 | Complete | 2026-03-26 |
| 34. Traceability and Verification Cleanup | 2/2 | Complete   | 2026-03-26 | — |
