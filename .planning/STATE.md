---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: Multi-Project & Solution-Level Graphs
status: unknown
last_updated: "2026-03-02T12:23:16.986Z"
progress:
  total_phases: 6
  completed_phases: 6
  total_plans: 11
  completed_plans: 11
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-01)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** Phase 16 — Solution MCP Tools

## Current Position

Phase: 16 of 17 (Solution MCP Tools) — IN PROGRESS
Current Plan: 16-02 COMPLETE (SolutionTools diff_snapshots)
Next Plan: Phase 16 COMPLETE — Phase 17
Last activity: 2026-03-02 — Phase 16 Plan 02 complete: diff_snapshots MCP tool added to SolutionTools (per-project diffs, projects added/removed, cross-project edge attribution); 6 new tests; 293 total passing

Progress: [████████░░░░░░░░░░░░] ~50% (14/17 phases)

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
- projectFilter on IKnowledgeQueryService.SearchAsync now applied in Phase 15 (service layer only, not ISearchIndex)
- crossProjectOnly on GetReferencesAsync filters by EdgeScope.CrossProject (exact match, case-sensitive)
- [Phase 15-02]: FQN heuristic: input without pipe '|' treated as FQN candidate; stable SymbolIds always contain '|'
- [Phase 15-02]: Same FQN in same project (multiple nodes) returns first match; disambiguation is cross-project only
- [Phase 15-02]: nodeProjectCache in GetReferences built via GetSymbolAsync per unique id (stays within IKnowledgeQueryService contract)
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
- [Phase 16-01]: explain_solution derives dependency DAG from CrossProject edge scope at query time — no pre-computed adjacency stored
- [Phase 16-01]: isSingleProject detection via unique ProjectOrigin count <= 1 (null ProjectOrigin falls back to snapshot.ProjectName)
- [Phase 16-01]: Doc coverage counts only public/protected/protectedInternal nodes of kinds: Type, Method, Property, Constructor, Delegate, Event, Field
- [Phase 16-01]: Stub nodes excluded from project stats — counted globally as totalStubNodeCount only
- [Phase 16-02]: ExtractProjectSnapshot filters IntraProject edges with From-or-To membership — same inclusive rule as explain_solution edge counting
- [Phase 16-02]: Cross-project edge equality keyed on (From.Value, To.Value, Kind) — Scope excluded since all are CrossProject by definition
- [Phase 16-02]: FormatEdgeEndpoint uses Project::SymbolId format; falls back to bare symbolId if node not in map
- [Phase 18-fix-diff-snapshots-collision]: SolutionTools.DiffSnapshots wire name changed to diff_solution_snapshots; C# method name unchanged

### Pending Todos

None.

### Blockers/Concerns

- Phase 14: MSBuildWorkspace memory profile at scale not well-documented — spike test recommended before committing to stub node cap heuristic
- Phase 17: Manifest-of-manifests design for incremental re-ingestion has no prior art in this codebase — design review required before implementation begins

## Session Continuity

Last session: 2026-03-02
Stopped at: Phase 16 Plan 02 complete (SolutionTools diff_snapshots: per-project diffs via SymbolGraphDiffer, projects added/removed, cross-project edge attribution; 6 new tests; 293 total passing).
Resume file: None
