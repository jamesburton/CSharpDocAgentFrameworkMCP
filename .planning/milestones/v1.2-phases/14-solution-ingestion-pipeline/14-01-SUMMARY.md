---
phase: 14-solution-ingestion-pipeline
plan: 01
subsystem: ingestion
tags: [roslyn, msbuildworkspace, symbol-graph, solution-ingestion, csharp]

# Dependency graph
requires:
  - phase: 13-core-domain-extensions
    provides: SymbolNode.ProjectOrigin field, SymbolGraphSnapshot.SolutionName field, NodeKind/EdgeScope enums
provides:
  - ISolutionIngestionService interface with IngestAsync contract
  - SolutionIngestionService implementation with language filtering, TFM dedup, partial-success, ProjectOrigin stamping
  - SolutionIngestionResult and ProjectIngestionStatus records
  - PipelineOverride seam for unit testing without MSBuild
  - 7 unit tests covering happy path, partial success, all-failed, store persistence, TFM ordering
affects:
  - 14-solution-ingestion-pipeline (plans 02+: MCP tool wiring, ingest_solution tool)
  - Phase 15: projectFilter filtering can now resolve ProjectOrigin values from ingested snapshots

# Tech tracking
tech-stack:
  added: []
  patterns:
    - PipelineOverride internal seam pattern for MSBuild-free unit testing (mirrors IngestionService pattern)
    - Sequential MSBuildWorkspace usage (not thread-safe — solution opened once, projects processed sequentially)
    - TFM dedup via GroupBy(FilePath) + OrderByDescending(ExtractTfmVersion)
    - ProjectOrigin stamping via LINQ Select with record with-expression

key-files:
  created:
    - src/DocAgent.McpServer/Ingestion/ISolutionIngestionService.cs
    - src/DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs
    - src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs
    - tests/DocAgent.Tests/SolutionIngestionServiceTests.cs
  modified: []

key-decisions:
  - "ExtractTfmVersion normalizes legacy net{NN} TFMs: net48 → 480, net472 → 472 so net48 > net472 for correct ordering"
  - "Modern TFMs biased by +100 in major component so net10.0 (110.0) always sorts above net48 (0.480)"
  - "SolutionIngestionService uses inline WalkNamespaceInline rather than delegating to RoslynSymbolGraphBuilder to avoid requiring a per-project MSBuildWorkspace inside an already-open solution workspace"
  - "PipelineOverride seam takes (slnPath, warnings, ct) → SolutionIngestionResult to allow full bypass of MSBuild in tests"
  - "InMemorySearchIndex used in tests (StubSearchIndex is a private nested class in IngestionServiceTests)"

patterns-established:
  - "TFM version extraction: internal static method + InternalsVisibleTo for direct test coverage"
  - "Solution-level ingestion uses sequential project processing (MSBuildWorkspace constraint)"
  - "Partial-success pattern: per-project status tracking, non-C# projects skipped with language in reason, null compilation = failed"

requirements-completed: [INGEST-01, INGEST-02, INGEST-03, INGEST-04]

# Metrics
duration: 30min
completed: 2026-03-01
---

# Phase 14 Plan 01: Solution Ingestion Service Summary

**ISolutionIngestionService with MSBuildWorkspace solution opening, TFM dedup, language filtering, ProjectOrigin stamping, partial-success handling, and SnapshotStore persistence**

## Performance

- **Duration:** ~30 min
- **Started:** 2026-03-01T17:36:15Z
- **Completed:** 2026-03-01T18:06:00Z
- **Tasks:** 2
- **Files modified:** 4 created

## Accomplishments

- ISolutionIngestionService interface and SolutionIngestionResult/ProjectIngestionStatus records cleanly separate contract from implementation
- SolutionIngestionService handles all 4 plan truths: language filtering (non-C# skipped with reason), TFM dedup (highest wins via ExtractTfmVersion), MSBuild failures (null compilation = "failed", other projects continue), ProjectOrigin stamped on all nodes
- PipelineOverride seam enables 7 unit tests with zero MSBuild dependency, mirroring the existing IngestionService pattern
- ExtractTfmVersion correctly orders net10.0 > net9.0 > net8.0 > net48 > net472 > (no TFM)
- All 261 tests pass (254 pre-existing + 7 new)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create result records and ISolutionIngestionService interface** - `25e9abf` (feat)
2. **Task 2: Implement SolutionIngestionService with full pipeline** - `b55a62f` (feat)

## Files Created/Modified

- `src/DocAgent.McpServer/Ingestion/ISolutionIngestionService.cs` - Interface contract with IngestAsync(slnPath, reportProgress, ct)
- `src/DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs` - SolutionIngestionResult + ProjectIngestionStatus records
- `src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs` - Full implementation: MSBuildWorkspace, TFM dedup, language filter, node walking, ProjectOrigin stamping, SnapshotStore + ISearchIndex integration
- `tests/DocAgent.Tests/SolutionIngestionServiceTests.cs` - 7 unit tests via PipelineOverride seam

## Decisions Made

- Used `InMemorySearchIndex` in tests (not `StubSearchIndex` which is a private nested class in `IngestionServiceTests`)
- `ExtractTfmVersion` normalizes legacy short TFMs: `net48` → 480, `net472` → 472 so ordering is correct
- Modern TFMs get major component biased by +100 so any `net{X}.{Y}` always beats any legacy `net{NN}`
- Inline `WalkNamespaceInline` (not delegating to `RoslynSymbolGraphBuilder`) to avoid opening a second per-project MSBuildWorkspace inside an already-open solution workspace

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed TFM comparison: net48 vs net472**
- **Found during:** Task 2 (test execution)
- **Issue:** Initial implementation compared raw legacy TFM numbers directly (48 < 472) causing incorrect ordering where net48 sorted below net472
- **Fix:** Normalized: short form TFMs (< 100) multiplied by 10, so net48 → 480 > net472 → 472
- **Files modified:** src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs
- **Verification:** ExtractTfmVersion_ModernTfmSortsAboveLegacy test passes
- **Committed in:** b55a62f (Task 2 commit)

**2. [Rule 1 - Bug] Fixed SymbolKind ambiguity between Microsoft.CodeAnalysis and DocAgent.Core**
- **Found during:** Task 2 (build)
- **Issue:** CS0104 ambiguous reference on `SymbolKind` — both Roslyn and DocAgent.Core define this type
- **Fix:** Added `using CoreSymbolKind = DocAgent.Core.SymbolKind;` alias, updated all usages
- **Files modified:** src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs
- **Verification:** Build succeeds with 0 errors 0 warnings
- **Committed in:** b55a62f (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (both Rule 1 bugs found during build/test)
**Impact on plan:** Both fixes required for correctness. No scope creep.

## Issues Encountered

None beyond the two auto-fixed bugs above.

## Next Phase Readiness

- ISolutionIngestionService is ready for wiring into the MCP tool layer (`ingest_solution` tool)
- SnapshotStore.SaveAsync integration tested via PipelineOverride seam
- TFM dedup logic proven via unit tests with net10.0, net9.0, net8.0, net48, net472 ordering

---
*Phase: 14-solution-ingestion-pipeline*
*Completed: 2026-03-01*

## Self-Check: PASSED

- [x] src/DocAgent.McpServer/Ingestion/ISolutionIngestionService.cs exists
- [x] src/DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs exists
- [x] src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs exists
- [x] tests/DocAgent.Tests/SolutionIngestionServiceTests.cs exists
- [x] Commit 25e9abf exists (Task 1)
- [x] Commit b55a62f exists (Task 2)
- [x] All 261 tests pass
