# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- **v1.1 Semantic Diff & Change Intelligence** — Phases 9-11

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

- [x] **Phase 9: Semantic Diff Engine** - Core diff types and algorithm for comparing two SymbolGraphSnapshots — detect signature, nullability, constraint, accessibility, and dependency changes (completed 2026-02-28)
- [ ] **Phase 10: Incremental Ingestion** - File change detection and partial re-ingestion — only re-process changed files with precise change tracking
- [ ] **Phase 11: Change Intelligence & Review** - MCP tools (review_changes, find_breaking_changes, explain_change) and unusual change review skill with worktree-based remediation proposals (depends on Phase 9)

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
| 10. Incremental Ingestion | 2/3 | In Progress|  | — |
| 11. Change Intelligence & Review | v1.1 | 0/? | Pending | — |

## Phase Details

### Phase 9: Semantic Diff Engine
**Goal**: Core diff types and algorithm for comparing two SymbolGraphSnapshots — detect signature, nullability, constraint, accessibility, and dependency changes
**Depends on**: Nothing (builds on v1.0 Core domain types)
**Requirements**: R-DIFF-ENGINE
**Success Criteria** (what must be TRUE):
  1. A `SymbolDiff` type captures all change categories (added, removed, modified symbols) with typed change details
  2. Diffing two `SymbolGraphSnapshot`s produces deterministic, complete results
  3. Signature changes (parameters, return types), nullability changes, generic constraint changes, accessibility changes, and dependency changes are all detected
  4. `dotnet test` passes with diff-specific tests covering each change category

### Phase 10: Incremental Ingestion
**Goal**: File change detection and partial re-ingestion — only re-process changed files with precise change tracking
**Depends on**: Nothing (builds on v1.0 Ingestion pipeline)
**Requirements**: R-INCR-INGEST
**Plans:** 2/3 plans executed

Plans:
- [ ] 10-01-PLAN.md — Core types (IngestionMetadata, FileHashManifest) and unit tests
- [ ] 10-02-PLAN.md — IncrementalIngestionEngine implementation and IngestionService integration
- [ ] 10-03-PLAN.md — Correctness integration tests (incremental == full re-ingestion)

**Success Criteria** (what must be TRUE):
  1. File change detection identifies added, modified, and removed source files between ingestion runs
  2. Only changed files are re-parsed and re-walked (unchanged symbols preserved from previous snapshot)
  3. The resulting snapshot is identical to a full re-ingestion (correctness guarantee)
  4. Change tracking metadata records which files changed and what symbols were affected

### Phase 11: Change Intelligence & Review
**Goal**: MCP tools (review_changes, find_breaking_changes, explain_change) and unusual change review skill with worktree-based remediation proposals
**Depends on**: Phase 9
**Requirements**: R-CHANGE-TOOLS, R-REVIEW-SKILL
**Success Criteria** (what must be TRUE):
  1. `review_changes` MCP tool returns structured findings comparing two snapshots
  2. `find_breaking_changes` identifies public API breaking changes (removed/changed public symbols)
  3. `explain_change` provides human-readable explanations of symbol-level diffs
  4. Unusual change detection flags suspicious patterns (semantic changes without doc/test updates, large blast-radius changes)
  5. Review findings include actionable remediation suggestions
