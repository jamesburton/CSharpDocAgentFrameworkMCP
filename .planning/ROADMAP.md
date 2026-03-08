# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- ✅ **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-18 (shipped 2026-03-02)
- ✅ **v1.3 Housekeeping** — Phases 19-22 (shipped 2026-03-04)
- ✅ **v1.5 Robustness** — Phases 23-27 (shipped 2026-03-08)

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

<details>
<summary>✅ v1.2 Multi-Project & Solution-Level Graphs (Phases 13-18) — SHIPPED 2026-03-02</summary>

- [x] Phase 13: Core Domain Extensions (2/2 plans) — completed 2026-03-01
- [x] Phase 14: Solution Ingestion Pipeline (2/2 plans) — completed 2026-03-01
- [x] Phase 14.1: Solution Graph Enrichment (2/2 plans) — completed 2026-03-01
- [x] Phase 15: Project-Aware Indexing & Query (2/2 plans) — completed 2026-03-01
- [x] Phase 16: Solution MCP Tools (2/2 plans) — completed 2026-03-02
- [x] Phase 18: Fix diff_snapshots Tool Name Collision (1/1 plan) — completed 2026-03-02

Full details: milestones/v1.2-ROADMAP.md

Known gap: Phase 17 (Incremental Solution Re-ingestion / INGEST-05) deferred to v1.3

</details>

<details>
<summary>✅ v1.3 Housekeeping (Phases 19-22) — SHIPPED 2026-03-04</summary>

- [x] Phase 19: Incremental Solution Re-ingestion (4/4 plans) — completed 2026-03-02
- [x] Phase 20: MSBuild Performance Benchmarks (2/2 plans) — completed 2026-03-03
- [x] Phase 21: Code and Audit Cleanup (1/1 plan) — completed 2026-03-03
- [x] Phase 22: Documentation Refresh (1/1 plan) — completed 2026-03-04

Full details: milestones/v1.3-ROADMAP.md

</details>

### ✅ v1.5 Robustness (Shipped 2026-03-08)

**Milestone Goal:** Harden the query pipeline, extend the tool surface, upgrade dependencies, and polish operational readiness — making DocAgentFramework production-grade.

**Build order:** Dependencies first (PKG), then performance (PERF), then server infrastructure (OPS-02/03) in parallel with performance, then API tools (API), then documentation last (OPS-01). This order reflects technical dependencies; user priority (operational polish first) governs scope decisions within each phase.

- [x] **Phase 23: Dependency Foundation** — Roslyn 4.14 upgrade and full NuGet audit (completed 2026-03-06)
- [x] **Phase 24: Query Performance** — O(1) symbol lookup, edge index, metadata caching (completed 2026-03-08)
- [x] **Phase 25: Server Infrastructure** — Startup validation and rate limiting (completed 2026-03-08)
- [x] **Phase 26: API Extensions** — Pagination, find_implementations, get_doc_coverage tools (completed 2026-03-08)
- [x] **Phase 27: Documentation Refresh** — CLAUDE.md updated to 14-tool surface (completed 2026-03-08)

## Phase Details

### Phase 23: Dependency Foundation
**Goal**: The build is clean on Roslyn 4.14.0 with no version conflicts and all NuGet dependencies audited for vulnerabilities
**Depends on**: Nothing (first phase of milestone)
**Requirements**: PKG-01, PKG-02
**Success Criteria** (what must be TRUE):
  1. `dotnet restore` completes with zero NU1107 warnings across all projects including Benchmarks and Analyzers
  2. The VersionOverride workaround in DocAgent.Tests.csproj is removed from the solution
  3. `NuGetAudit` is enabled in Directory.Build.props and `dotnet restore` reports no known vulnerabilities
  4. All five Microsoft.CodeAnalysis.* packages are at 4.14.0 and the package audit baseline is recorded
**Plans:** 1/1 plans complete

Plans:
- [x] 23-01-PLAN.md — Roslyn 4.14.0 upgrade, csproj cleanup, and NuGetAudit enablement (completed 2026-03-06)

### Phase 24: Query Performance
**Goal**: Symbol lookups and edge traversals operate in O(1) time using pre-built index dictionaries, eliminating linear scans from the hot query path
**Depends on**: Phase 23
**Requirements**: PERF-01, PERF-02, PERF-03
**Success Criteria** (what must be TRUE):
  1. `GetSymbolAsync` and symbol existence checks use dictionary lookup instead of list scan (verifiable by code inspection and existing benchmark baseline holding)
  2. `GetReferencesAsync` edge traversal uses pre-built `_edgesByFrom`/`_edgesByTo` dictionaries built at index time
  3. `SearchAsync` metadata retrieval uses a TTL-cached node map instead of per-hit async disk reads
  4. All 330 existing tests continue to pass with identical output (determinism preserved — no Dictionary ordering in serialisation paths)
**Plans:** 1 plan

Plans:
- [x] 24-01-PLAN.md — SnapshotLookup cache with O(1) symbol, edge, and metadata lookups (completed 2026-03-08)

### Phase 25: Server Infrastructure
**Goal**: The MCP server fails fast on invalid startup configuration and throttles tool invocations to prevent stuck-agent retry storms
**Depends on**: Phase 23
**Requirements**: OPS-02, OPS-03
**Success Criteria** (what must be TRUE):
  1. Starting the server with AllowedPaths empty or ArtifactsDir non-writable prints a diagnostic error to stderr and exits non-zero before accepting any tool calls
  2. A client that exceeds the configured token-bucket limit receives a structured error response (not an unhandled exception) and the server continues operating normally for subsequent calls
  3. The rate limiter is a DI singleton — ingestion tool calls are not counted against the query rate limit
  4. The startup validator is unit-testable in isolation via ServiceCollection without requiring Aspire or a running process
**Plans:** 2/2 plans complete

Plans:
- [x] 25-01-PLAN.md — Startup configuration validation with fail-fast on invalid config (completed 2026-03-08)
- [x] 25-02-PLAN.md — Token-bucket rate limiting with separate query/ingestion buckets (completed 2026-03-08)

### Phase 26: API Extensions
**Goal**: Agents can paginate large reference lists, navigate to implementations of interfaces/base classes, and query documentation coverage metrics — all via MCP tools
**Depends on**: Phase 24, Phase 25
**Requirements**: API-01, API-02, API-03
**Success Criteria** (what must be TRUE):
  1. `get_references` called without `offset`/`limit` returns the same response shape as before this phase (no silent truncation of existing callers)
  2. `get_references` called with explicit `offset`/`limit` returns a paginated envelope with `totalCount`, consistent with `search_symbols` pagination behavior
  3. `find_implementations` returns all types implementing a given interface or deriving from a base class, with stub nodes (NodeKind.Stub) excluded from results
  4. `get_doc_coverage` returns documentation coverage metrics grouped by project, namespace, and symbol kind, derived from snapshot post-processing
  5. All new tools are secured with PathAllowlist enforcement matching the pattern of existing tools
**Plans:** 2/2 plans complete

Plans:
- [x] 26-01-PLAN.md — Pagination for get_references and find_implementations tool (API-01, API-02) (completed 2026-03-08)
- [x] 26-02-PLAN.md — Documentation coverage tool get_doc_coverage (API-03) (completed 2026-03-08)

### Phase 27: Documentation Refresh
**Goal**: CLAUDE.md accurately documents all 14 MCP tools with correct parameter signatures, format options, and projectFilter behavior, enabling agents to call tools correctly on first attempt
**Depends on**: Phase 26
**Requirements**: OPS-01
**Success Criteria** (what must be TRUE):
  1. CLAUDE.md lists all 14 MCP tools (12 existing + find_implementations + get_doc_coverage) with their parameters, return shapes, and format options
  2. Each tool entry is verified against the actual `[McpServerTool]`-decorated method signatures in the codebase — no provisional or stale signatures
  3. The projectFilter parameter is documented for all tools that accept it
**Plans:** 1/1 plans complete

Plans:
- [x] 27-01-PLAN.md — Update CLAUDE.md with complete 14-tool MCP reference (completed 2026-03-08)

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
