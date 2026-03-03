# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- ✅ **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-18 (shipped 2026-03-02)
- 🚧 **v1.3 Housekeeping** — Phases 19-22 (in progress)

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

### 🚧 v1.3 Housekeeping (In Progress)

**Milestone Goal:** Clear accumulated backlog — deliver deferred INGEST-05, benchmark MSBuild performance, remove stale code artifacts, and refresh documentation to reflect v1.0-v1.2 reality.

- [x] **Phase 19: Incremental Solution Re-ingestion** - Per-project skip in SolutionIngestionService with dependency cascade and correctness guarantee (completed 2026-03-02)
- [x] **Phase 20: MSBuild Performance Benchmarks** - Latency/memory baselines and regression guard for solution ingestion (completed 2026-03-03)
- [x] **Phase 21: Code and Audit Cleanup** - Remove stale TODOs and resolve v1.2 audit artifact issues (completed 2026-03-03)
- [ ] **Phase 22: Documentation Refresh** - Align Architecture.md, Plan.md, Testing.md to v1.0-v1.2 shipped reality

## Phase Details

### Phase 19: Incremental Solution Re-ingestion
**Goal**: Solution re-ingestion skips unchanged projects, producing a byte-identical result to full re-ingestion for unchanged input
**Depends on**: Phase 18 (v1.2 complete)
**Requirements**: INGEST-01, INGEST-02, INGEST-03, INGEST-04, INGEST-05
**Success Criteria** (what must be TRUE):
  1. Running `ingest_solution` twice with no file changes skips all projects on the second call (observable via logs/telemetry)
  2. Changing one project's source file causes only that project (and its dependents) to re-ingest; unrelated projects are skipped
  3. Per-project manifests use path-based keys so two projects with the same name in different directories never collide
  4. Stub nodes from the prior ingestion are correctly regenerated after an incremental run — no accumulation of stale stubs
  5. Incremental solution result is byte-identical to a full re-ingestion when no files changed (verified by determinism test)
**Plans**: 3 plans

Plans:
- [ ] 19-01-PLAN.md — SolutionManifestStore + DependencyCascade helpers with tests
- [ ] 19-02-PLAN.md — IncrementalSolutionIngestionService implementation with stub lifecycle
- [ ] 19-03-PLAN.md — Byte-identity determinism test (INGEST-05) + telemetry

### Phase 20: MSBuild Performance Benchmarks
**Goal**: MSBuild workspace open latency and solution ingestion memory usage are measured, baselined, and guarded against regression
**Depends on**: Phase 19
**Requirements**: PERF-01, PERF-02, PERF-03
**Success Criteria** (what must be TRUE):
  1. A benchmark suite exists that measures per-project compilation latency and can be run on demand
  2. Memory high-water mark during solution ingestion is captured and recorded as a baseline
  3. A regression guard test fails the build if ingestion latency or memory exceeds the defined threshold
**Plans**: TBD

Plans:
- [ ] 20-01: TBD

### Phase 21: Code and Audit Cleanup
**Goal**: All known stale comments are removed, v1.2 audit artifact issues are resolved, and integration gaps from v1.3 audit are fixed
**Depends on**: Phase 19
**Requirements**: QUAL-01, QUAL-02, QUAL-03
**Gap Closure:** Closes requirement gaps QUAL-01/02/03 + integration gaps from v1.3 audit
**Success Criteria** (what must be TRUE):
  1. `InMemorySearchIndex.cs` no longer contains the stale "TODO: replace with BM25" comment
  2. `KnowledgeQueryService.cs` line 215 no longer contains the stale "stub" comment
  3. v1.2 audit artifacts have clean frontmatter and no remaining documentation gaps flagged
  4. `IncrementalNoChange` benchmark uses `IncrementalSolutionIngestionService` (not bare `SolutionIngestionService`)
  5. `Program.cs` registers `metrics.AddMeter("DocAgent.Ingestion")` for OTel counter collection
**Plans**: 1 plan

Plans:
- [ ] 21-01-PLAN.md — Remove stale comments, fix audit frontmatter, wire benchmark to decorator, register OTel meter

### Phase 22: Documentation Refresh
**Goal**: Architecture.md, Plan.md, and Testing.md accurately reflect the v1.0-v1.2 shipped codebase and current test suite
**Depends on**: Phase 21
**Requirements**: DOCS-01, DOCS-02, DOCS-03
**Gap Closure:** Closes requirement gaps DOCS-01/02/03 from v1.3 audit
**Success Criteria** (what must be TRUE):
  1. Architecture.md names all 6 projects and all 12 MCP tools correctly
  2. Plan.md reflects v1.0-v1.2 as shipped — no phantom features or missing accomplishments
  3. Testing.md states the current test count and describes the strategy as it actually exists
**Plans**: 1 plan

Plans:
- [ ] 22-01-PLAN.md — Rewrite Architecture.md, Plan.md, and Testing.md to match shipped reality

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
| 19. Incremental Solution Re-ingestion | 4/4 | Complete    | 2026-03-02 | - |
| 20. MSBuild Performance Benchmarks | 2/2 | Complete    | 2026-03-03 | - |
| 21. Code and Audit Cleanup | 1/1 | Complete    | 2026-03-03 | - |
| 22. Documentation Refresh | v1.3 | 0/? | Not started | - |
