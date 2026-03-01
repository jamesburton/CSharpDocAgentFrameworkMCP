# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- 🚧 **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-17 (in progress)

## Phases

<details>
<summary>✅ v1.0 MVP (Phases 1-8) — SHIPPED 2026-02-28</summary>

- [x] Phase 1: Core Domain (3/3 plans) — completed 2026-02-26
- [x] Phase 2: Ingestion Pipeline (5/5 plans) — completed 2026-02-26
- [x] Phase 3: BM25 Search Index (2/2 plans) — completed 2026-02-26
- [x] Phase 4: Query Facade (2/2 plans) — completed 2026-02-26
- [x] Phase 5: MCP Server + Security (3/3 plans) — completed 2026-02-27
- [x] Phase 7: Runtime Integration Wiring (3/3 plans) — completed 2026-02-27
- [x] Phase 6: Analysis + Hosting (4/4 plans) — completed 2026-02-28
- [x] Phase 8: Ingestion Runtime Trigger (2/2 plans) — completed 2026-02-28

Full details: milestones/v1.0-ROADMAP.md

</details>

<details>
<summary>✅ v1.1 Semantic Diff & Change Intelligence (Phases 9-12) — SHIPPED 2026-03-01</summary>

- [x] Phase 9: Semantic Diff Engine (3/3 plans) — completed 2026-02-28
- [x] Phase 10: Incremental Ingestion (3/3 plans) — completed 2026-02-28
- [x] Phase 11: Change Intelligence & Review (2/2 plans) — completed 2026-02-28
- [x] Phase 12: ChangeTools Security Gate (1/1 plan) — completed 2026-03-01

Full details: milestones/v1.1-ROADMAP.md

</details>

### 🚧 v1.2 Multi-Project & Solution-Level Graphs (In Progress)

**Milestone Goal:** Agents can query a unified symbol graph spanning an entire .NET solution, with cross-project dependency tracing and solution-aware MCP tools.

- [x] **Phase 13: Core Domain Extensions** — Extend domain types to carry solution-level identity and cross-project metadata (completed 2026-03-01)
- [x] **Phase 14: Solution Ingestion Pipeline** — Ingest full .sln files; resolve dependencies; populate enriched snapshots (completed 2026-03-01)
- [x] **Phase 14.1: Solution Graph Enrichment** — Populate SolutionSnapshot, classify cross-project edges, synthesize stub nodes (gap closure) (completed 2026-03-01)
- [ ] **Phase 15: Project-Aware Indexing & Query** — Thread project attribution through BM25 index and query service
- [ ] **Phase 16: Solution MCP Tools** — Expose solution-level tools and update existing tools with solution awareness
- [ ] **Phase 17: Incremental Solution Re-ingestion** — Re-ingest only changed projects within a solution

## Phase Details

### Phase 13: Core Domain Extensions
**Goal**: Domain types carry solution-level identity, cross-project edge scopes, and stub-node flags — giving every downstream layer a backward-compatible foundation to build on
**Depends on**: Nothing (builds on shipped v1.1 types)
**Requirements**: GRAPH-01, GRAPH-02, GRAPH-03, GRAPH-04, GRAPH-05, GRAPH-06
**Success Criteria** (what must be TRUE):
  1. `SymbolNode` has nullable `ProjectOrigin` and `IsStub` bool; existing v1.0/v1.1 MessagePack artifacts deserialize without error (null/false defaults)
  2. `SymbolEdge` carries an `EdgeScope` enum with `IntraProject` as the default; existing edges round-trip cleanly
  3. `SymbolGraphSnapshot` has nullable `SolutionName` and an empty-by-default `Projects` list holding `ProjectEntry` records with project name, path, and dependency references
  4. `IKnowledgeQueryService.SearchAsync` accepts an optional `projectFilter` parameter without breaking existing callers
  5. All 220+ existing tests pass without modification after the type changes land
**Plans:** 2/2 plans complete
Plans:
- [ ] 13-01-PLAN.md — Extend existing types (SymbolNode, SymbolEdge, SymbolGraphSnapshot) with enums + optional fields; update IKnowledgeQueryService; backward-compat tests
- [ ] 13-02-PLAN.md — New solution aggregate types (SolutionSnapshot, ProjectEntry, ProjectEdge) with MessagePack roundtrip tests

### Phase 14: Solution Ingestion Pipeline
**Goal**: An agent can ingest an entire .sln in one call; non-C# projects are skipped gracefully; MSBuild failures are detected and reported; the resulting snapshot carries per-node project attribution and is PathAllowlist-secured
**Depends on**: Phase 13
**Requirements**: INGEST-01, INGEST-02, INGEST-03, INGEST-04, INGEST-06
**Success Criteria** (what must be TRUE):
  1. Calling `ingest_solution` with a valid `.sln` path produces a `SymbolGraphSnapshot` where every node has a non-null `ProjectOrigin` matching its source project
  2. Non-C# projects (e.g., F#, VB) in the solution are skipped and a warning is logged; the snapshot still contains all C# symbols
  3. Multi-targeting projects (e.g., `net10.0;net48`) produce exactly one set of nodes, not duplicates
  4. `workspace.Diagnostics` failures are logged and projects with null compilations are skipped, with the tool returning a partial-success response describing skipped projects
  5. Calling `ingest_solution` with a path outside the configured PathAllowlist returns the same opaque not-found denial as all other secured tools
**Plans:** 2/2 plans complete
Plans:
- [ ] 14-01-PLAN.md — SolutionIngestionService: result types, interface, implementation with language filter/TFM dedup/partial-success, unit tests
- [ ] 14-02-PLAN.md — ingest_solution MCP tool method, DI wiring, PathAllowlist security, tool-level tests

### Phase 14.1: Solution Graph Enrichment
**Goal**: Solution ingestion produces a fully populated `SolutionSnapshot` with project dependency DAG, cross-project edges carry `EdgeScope.CrossProject`, and external type references create `NodeKind.Stub` nodes — closing all integration gaps between Phase 13 domain types and Phase 14 ingestion
**Depends on**: Phase 14
**Requirements**: GRAPH-01, GRAPH-02, GRAPH-03, GRAPH-04, GRAPH-05
**Gap Closure**: Closes integration gaps from v1.2 milestone audit
**Success Criteria** (what must be TRUE):
  1. `SolutionIngestionService.IngestAsync` constructs and persists a `SolutionSnapshot` containing `ProjectEntry` records with dependency lists and `ProjectEdge` records modeling the project DAG
  2. Edges between symbols in different projects carry `EdgeScope.CrossProject`; edges within the same project carry `EdgeScope.IntraProject`
  3. Type references to external/NuGet types create `SymbolNode`s with `NodeKind = NodeKind.Stub` and `IsExternal = true`
  4. All existing 266+ tests continue to pass after enrichment changes
  5. New tests verify SolutionSnapshot population, cross-project edge classification, and stub node creation
**Plans:** 2/2 plans complete
Plans:
- [ ] 14.1-01-PLAN.md — Enrich SolutionIngestionService with SolutionSnapshot builder, edge classification, stub synthesis + tests
- [ ] 14.1-02-PLAN.md — Stub node filtering in BM25SearchIndex and InMemorySearchIndex + dedicated tests

### Phase 15: Project-Aware Indexing & Query
**Goal**: BM25 search results carry project attribution; agents can filter results to a single project or cross-project references without changing existing query contracts
**Depends on**: Phase 14.1
**Requirements**: TOOLS-01, TOOLS-02, TOOLS-03, TOOLS-06
**Success Criteria** (what must be TRUE):
  1. `search_symbols` called against a solution snapshot returns results from all projects in a single ranked list
  2. `search_symbols` called with an optional `project` filter returns only symbols from that project
  3. `get_symbol` resolves a fully qualified name that exists in any project in the solution, not just the first project processed
  4. `get_references` with optional `crossProjectOnly` filter returns only edges whose `EdgeScope` is `CrossProject`, enabling "who across project boundaries calls this?" queries
**Plans**: TBD

### Phase 16: Solution MCP Tools
**Goal**: Agents have a solution-level architecture overview tool and can diff solution snapshots; existing tools become solution-aware without breaking current MCP clients
**Depends on**: Phase 15
**Requirements**: TOOLS-04, TOOLS-05
**Success Criteria** (what must be TRUE):
  1. `explain_solution` returns a structured overview: project list with node/edge counts, the project dependency DAG, per-project doc coverage percentages, and total stub node count
  2. `diff_snapshots` called with two `SolutionSnapshot`s produces a diff that spans all projects, including cross-project edge additions and removals
  3. `SolutionTools` enforces PathAllowlist on all operations with opaque not-found denial, matching the pattern of `DocTools` and `ChangeTools`
**Plans**: TBD

### Phase 17: Incremental Solution Re-ingestion
**Goal**: Agents can trigger re-ingestion of only the projects that changed within a solution, preserving previously-ingested data for unchanged projects
**Depends on**: Phase 14
**Requirements**: INGEST-05
**Success Criteria** (what must be TRUE):
  1. Changing a single file in one project and calling `ingest_solution` re-parses only that project; unchanged projects retain their previous snapshot data byte-for-byte
  2. The re-ingested solution snapshot is identical to a full re-ingestion of the same solution state
  3. The manifest-of-manifests store does not corrupt or overwrite the existing `SnapshotStore` content-hash scheme
**Plans**: TBD

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
| 13. Core Domain Extensions | 2/2 | Complete   | 2026-03-01 | - |
| 14. Solution Ingestion Pipeline | 2/2 | Complete    | 2026-03-01 | - |
| 14.1 Solution Graph Enrichment | 2/2 | Complete   | 2026-03-01 | - |
| 15. Project-Aware Indexing & Query | v1.2 | 0/? | Not started | - |
| 16. Solution MCP Tools | v1.2 | 0/? | Not started | - |
| 17. Incremental Solution Re-ingestion | v1.2 | 0/? | Not started | - |
