# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- ✅ **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-18 (shipped 2026-03-02)
- ✅ **v1.3 Housekeeping** — Phases 19-22 (shipped 2026-03-04)
- ✅ **v1.5 Robustness** — Phases 23-27 (shipped 2026-03-08)
- [ ] **v2.0 TypeScript Language Support** — Phases 28-31 (in progress)

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
- [ ] **Phase 30: MCP Integration and Incremental Ingestion** - ingest_typescript tool, incremental file hashing, BM25 tokenization
- [ ] **Phase 31: Verification and Hardening** - Determinism, cross-tool validation, security, performance profiling

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
**Plans**: 2 plans

Plans:
- [ ] 30-01-PLAN.md — Ingest TypeScript MCP Tool and Incremental Ingestion
- [ ] 30-02-PLAN.md — Search Refinement and E2E Verification

### Phase 31: Verification and Hardening
**Goal**: The TypeScript ingestion pipeline is proven deterministic, secure, and performant through comprehensive validation against real-world projects
**Depends on**: Phase 30
**Requirements**: VERF-01, VERF-02, VERF-03, VERF-04
**Success Criteria** (what must be TRUE):
  1. Golden-file tests confirm that ingesting the same TypeScript project twice produces byte-identical SymbolGraphSnapshot output
  2. A cross-tool validation suite exercises all 14 MCP tools against TypeScript snapshots and verifies correct results (search hits, symbol details, reference traversal, diff output, change review)
  3. Security validation confirms PathAllowlist enforcement on ingest_typescript, no absolute paths leak in SymbolNode.Span fields, and audit logging captures TypeScript ingestion events
  4. Performance profiling on a 500+ file TypeScript project completes within established baseline thresholds and identifies no IPC serialization bottlenecks
**Plans**: TBD

Plans:
- [x] 31-01-PLAN.md — Performance and Large-Scale Validation
- [x] 31-02-PLAN.md — Robustness and Security Hardening

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
| 29. Core Symbol Extraction | 3/3 | Complete   | 2026-03-24 | — |
| 30. MCP Integration and Incremental Ingestion | v2.0 | 2/2 | Complete | 2026-03-08 |
| 31. Verification and Hardening | v2.0 | 2/2 | Complete | 2026-03-08 |
