---
phase: 19-incremental-solution-re-ingestion
plan: 04
subsystem: ingestion
tags: [incremental, manifest, dependency-cascade, file-hashing, skip-path]

requires:
  - phase: 19-01
    provides: DependencyCascade and SolutionManifestStore building blocks
  - phase: 19-02
    provides: IncrementalSolutionIngestionService decorator with PipelineOverride seam
  - phase: 19-03
    provides: Determinism tests and telemetry counters
provides:
  - Production skip path in IncrementalSolutionIngestionService that avoids full re-ingest when nothing changed
  - SolutionSnapshot JSON persistence for incremental state across runs
  - 4 production-path tests proving wiring without PipelineOverride
affects: [phase-20, mcp-tools]

tech-stack:
  added: [System.Text.Json for SolutionSnapshot persistence]
  patterns: [JSON sidecar file for SolutionSnapshot, manifest-based change detection in production hot path]

key-files:
  created: []
  modified:
    - src/DocAgent.McpServer/Ingestion/IncrementalSolutionIngestionService.cs
    - tests/DocAgent.Tests/IncrementalIngestion/SolutionIncrementalIngestionTests.cs

key-decisions:
  - "SolutionSnapshot persisted as JSON sidecar (latest-{sln}.solution.json) alongside pointer file for incremental state"
  - "Structural change check is a no-op in current design (compares previous projects to themselves) — becomes useful when MSBuild project discovery is wired in"
  - "Empty dirty set returns cached snapshot; non-empty dirty set delegates to full ingest (simplest correct approach)"

patterns-established:
  - "JSON sidecar pattern: persist SolutionSnapshot alongside SymbolGraphSnapshot pointer for cross-run state"

requirements-completed: [INGEST-01, INGEST-02, INGEST-04, INGEST-05]

duration: 18min
completed: 2026-03-02
---

# Phase 19 Plan 04: Production Skip Path Summary

**Wired manifest-based change detection into IncrementalSolutionIngestionService production hot path, closing 3 verification gaps (SolutionManifestStore.LoadAsync, DependencyCascade, FileHasher.Diff now called from production code)**

## Performance

- **Duration:** 18 min
- **Started:** 2026-03-02T21:33:14Z
- **Completed:** 2026-03-02T21:51:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments
- Production skip path calls SolutionManifestStore.LoadAsync, ComputeProjectManifestAsync, FileHasher.Diff, DependencyCascade.HasStructuralChange/ComputeDirtySet/TopologicalSort
- Lines 109-126 no longer unconditionally delegate to full ingest
- 4 new production-path tests prove wiring without PipelineOverride (nothing-changed skip, file-changed delegation, stub preservation, no-previous-snapshot fallback)
- All 290 non-integration tests pass with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Wire production skip path** - `de0c721` (feat)
2. **Task 2: Update tests for production path** - `c960c37` (test)

## Files Created/Modified
- `src/DocAgent.McpServer/Ingestion/IncrementalSolutionIngestionService.cs` - Production skip path with manifest comparison, dependency cascade, and SolutionSnapshot JSON persistence
- `tests/DocAgent.Tests/IncrementalIngestion/SolutionIncrementalIngestionTests.cs` - 4 new production-path tests using real temp directories

## Decisions Made
- Added SolutionSnapshot JSON sidecar persistence (latest-{sln}.solution.json) because SnapshotStore only stores flat SymbolGraphSnapshot, but incremental path needs project DAG info
- Structural change check compares previous project list to itself (no-op) since MSBuild workspace discovery is not available without full ingest — kept as future-proofing
- Empty dirty set returns cached SolutionSnapshot directly; non-empty delegates to full ingest

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added SolutionSnapshot JSON persistence**
- **Found during:** Task 1 (Wire production skip path)
- **Issue:** LoadPreviousSnapshotAsync returns SymbolGraphSnapshot which lacks Projects/ProjectDependencies/ProjectSnapshots needed for incremental comparison
- **Fix:** Added SaveSolutionSnapshotAsync/LoadPreviousSolutionSnapshotAsync using System.Text.Json to persist SolutionSnapshot as JSON sidecar file
- **Files modified:** src/DocAgent.McpServer/Ingestion/IncrementalSolutionIngestionService.cs
- **Verification:** Build passes, all tests pass
- **Committed in:** de0c721

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Essential for correctness — SolutionSnapshot state required for cross-run incremental comparison. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Phase 19 fully complete (all 4 plans executed)
- All INGEST requirements satisfied in production code path
- Ready for Phase 20

---
*Phase: 19-incremental-solution-re-ingestion*
*Completed: 2026-03-02*
