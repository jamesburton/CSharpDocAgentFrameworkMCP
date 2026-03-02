# Phase 14: Solution Ingestion Pipeline - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Ingest full `.sln` files via an `ingest_solution` MCP tool. Resolve project dependencies, populate enriched snapshots with per-node project attribution (`ProjectOrigin`). Non-C# projects are handled gracefully. MSBuild failures produce partial-success results. PathAllowlist security is enforced.

</domain>

<decisions>
## Implementation Decisions

### Error & partial success behavior
- Skip and continue: if a project fails to compile, skip it and ingest everything else; return a partial snapshot with a list of skipped projects and reasons
- Structured per-project status in response: each project gets `{name, status: 'ok'|'skipped'|'failed', reason?, nodeCount?}` — machine-readable
- Any project is enough: even 1 out of N projects succeeding produces a valid partial snapshot
- Surface MSBuild diagnostic warnings in the tool response so agents know about potential issues (missing optional refs, deprecated APIs)

### Non-C# project handling
- Skip non-C# projects with warning — include them in response as 'skipped (unsupported language)', don't attempt parsing
- All non-C# projects treated uniformly (F#, VB, C++/CLI — no special cases)
- Always ingest test projects — agents might want to query test structure and coverage
- Preserve cross-language dependency edges in ProjectEdge graph — the DAG shows all project references regardless of language, giving agents the full picture

### Multi-targeting & TFM selection
- Pick the highest/newest TFM (e.g., net10.0 over net48) — most APIs available, matches primary target
- Record the chosen TFM in snapshot metadata so agents know which framework view they're seeing
- No TFM fallback: if the chosen TFM doesn't compile, treat the project as failed (consistent with error handling above)
- Conditional compilation resolves naturally via Roslyn for the chosen TFM — #if branches produce a TFM-specific view

### Ingestion tool response shape
- Structured summary on success: solution name, project count, total nodes/edges, per-project status array, warnings list, snapshot ID/path
- Require explicit `.sln` path — no directory auto-discovery (avoids ambiguity with multiple .sln files)
- Auto-persist to SnapshotStore like existing `ingest_project` — consistent behavior, agents get a snapshot ID back
- PathAllowlist check on the `.sln` path only — if the .sln is allowed, all projects within it are implicitly allowed

### Claude's Discretion
- MSBuildWorkspace initialization and lifecycle management
- Internal batching/parallelization of project compilation
- TFM parsing and comparison logic
- Warning severity classification
- SnapshotStore key format for solution-level snapshots

</decisions>

<specifics>
## Specific Ideas

- Follow existing `ingest_project` patterns for SnapshotStore persistence and PathAllowlist enforcement
- MSBuildLocator.RegisterDefaults() must be called before any MSBuild type load (decision from earlier phases)
- ProjectOrigin on every SymbolNode must match the source project's name from the .sln

</specifics>

<deferred>
## Deferred Ideas

- None — discussion stayed within phase scope

</deferred>

---

*Phase: 14-solution-ingestion-pipeline*
*Context gathered: 2026-03-01*
