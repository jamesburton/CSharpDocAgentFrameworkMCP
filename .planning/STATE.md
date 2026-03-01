---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: Multi-Project & Solution-Level Graphs
status: unknown
last_updated: "2026-03-01T19:42:07.331Z"
progress:
  total_phases: 3
  completed_phases: 3
  total_plans: 6
  completed_plans: 6
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-01)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** Phase 14 — Solution Ingestion Pipeline

## Current Position

Phase: 14.1 of 17 (Solution Graph Enrichment) — IN PROGRESS
Current Plan: 14.1-02 COMPLETE (stub node filtering in BM25SearchIndex and InMemorySearchIndex)
Next Plan: 14.1-03 (if exists, else phase complete)
Last activity: 2026-03-01 — Phase 14.1 Plan 02 complete: NodeKind.Stub nodes excluded at index construction time from both BM25SearchIndex and InMemorySearchIndex; 4 new tests; 277 total passing

Progress: [████████░░░░░░░░░░░░] ~47% (13.5/17 phases)

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting v1.2:

- Single flat snapshot model preserved (no per-project sub-snapshots — breaks existing contracts)
- MessagePack ContractlessStandardResolver handles backward compat via null/false/empty-list defaults on all new fields
- MSBuildLocator.RegisterDefaults() must be first statement in Program.cs before any MSBuild type load
- Serve v1.0/v1.1 artifacts as-is with ProjectOrigin = null; require explicit ingest_solution call for v1.2 enrichment
- Stub nodes capped to direct PackageReference assemblies only (not transitive closure) to prevent index bloat
- NodeKind.Real=0 and EdgeScope.IntraProject=0 chosen as enum defaults for MessagePack backward compat with old artifacts
- projectFilter on IKnowledgeQueryService.SearchAsync accepted but not applied until Phase 15
- [Phase 13-core-domain-extensions]: SolutionSnapshot holds per-project SymbolGraphSnapshots as-is (not merged) to preserve project boundaries
- [Phase 14-01]: ExtractTfmVersion normalizes legacy net{NN}: short form (< 100) × 10, so net48 → 480 > net472 → 472; modern net{X}.{Y} biased by major+100 so always above legacy
- [Phase 14-01]: Inline WalkNamespaceInline in SolutionIngestionService (not delegating to RoslynSymbolGraphBuilder) to avoid second per-project MSBuildWorkspace inside open solution
- [Phase 14-01]: PipelineOverride seam takes (slnPath, warnings, ct) → SolutionIngestionResult for full MSBuild bypass in unit tests
- [Phase 14-solution-ingestion-pipeline]: IngestSolution mirrors IngestProject security pattern exactly: same allowlist message, progress token extraction, error handling
- [Phase 14.1-01]: EdgeScope classified at WalkTypeInline call site (construction time), not via post-classification pass — locked design constraint met
- [Phase 14.1-01]: ProjectWalkContext readonly record struct with shared seenStubIds HashSet across all projects deduplicates external type stub nodes
- [Phase 14.1-01]: Primitive filter with 30 common framework types (System.String, Task, IEnumerable<T>, etc.) prevents stub bloat
- [Phase 14.1-01]: SolutionIngestionResult.Snapshot defaults to null — fully backward-compatible with existing positional record callers
- [Phase 14.1]: Stub filter applied at index construction in both WriteDocuments and PopulateNodes in BM25, and IndexAsync in InMemory — covers SearchAsync and GetAsync exclusion paths without query-time overhead

### Pending Todos

None.

### Blockers/Concerns

- Phase 14: MSBuildWorkspace memory profile at scale not well-documented — spike test recommended before committing to stub node cap heuristic
- Phase 17: Manifest-of-manifests design for incremental re-ingestion has no prior art in this codebase — design review required before implementation begins

## Session Continuity

Last session: 2026-03-01
Stopped at: Phase 14.1 Plan 02 complete (stub node filtering: BM25SearchIndex + InMemorySearchIndex exclude NodeKind.Stub at index construction time + 4 tests + SUMMARY.md).
Resume file: None
