---
phase: 10-incremental-ingestion
plan: 02
subsystem: ingestion
tags: [csharp, dotnet, incremental-ingestion, sha256, symbol-merge, roslyn]

requires:
  - phase: 10-01
    provides: IngestionMetadata, FileHashManifest, ManifestDiff, FileHasher contracts

provides:
  - IncrementalIngestionEngine: orchestrates change detection, selective re-parse, and symbol merge
  - IngestionService updated with forceFullReingestion parameter and incremental engine integration
  - SnapshotStore.ArtifactsDir property for cross-class access

affects:
  - 10-03 (test suite exercises IncrementalIngestionEngine end-to-end)

tech-stack:
  added: []
  patterns:
    - BuildOverride hook on IncrementalIngestionEngine for test isolation (same pattern as PipelineOverride in IngestionService)
    - Symbol merge via HashSet deduplication (new nodes win over old by SymbolId)
    - Removed files included in change set alongside ChangedFiles to trigger project re-parse
    - InternalsVisibleTo added to DocAgent.Ingestion.csproj to expose internal test hooks

key-files:
  created:
    - src/DocAgent.Ingestion/IncrementalIngestionEngine.cs
    - tests/DocAgent.Tests/IncrementalIngestion/IncrementalIngestionEngineTests.cs
  modified:
    - src/DocAgent.Ingestion/SnapshotStore.cs
    - src/DocAgent.Ingestion/DocAgent.Ingestion.csproj
    - src/DocAgent.McpServer/Ingestion/IngestionService.cs
    - src/DocAgent.McpServer/Ingestion/IIngestionService.cs
    - tests/DocAgent.Tests/IngestionToolTests.cs

key-decisions:
  - "RemovedFiles included in change detection set alongside ChangedFiles (added+modified) so deleted .cs files trigger project re-parse"
  - "BuildOverride uses (ProjectInventory, DocInputSet, CancellationToken) signature matching ISymbolGraphBuilder.BuildAsync for natural parity"
  - "SnapshotStore.ArtifactsDir exposed as public property to avoid constructor coupling in IngestionService"
  - "forceFullReingestion added as last parameter with default false to IIngestionService for backward compatibility"
  - "Manifest saved AFTER successful snapshot construction (Pitfall 4 from research — no partial state on failure)"

duration: 20min
completed: 2026-02-28
---

# Phase 10 Plan 02: IncrementalIngestionEngine Summary

**IncrementalIngestionEngine implemented: SHA-256 manifest diff drives selective Roslyn re-parse, symbols merged from previous snapshot, IngestionMetadata records all file changes per run**

## Performance

- **Duration:** ~20 min
- **Completed:** 2026-02-28
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments

- Implemented `IncrementalIngestionEngine` in `DocAgent.Ingestion` — the core incremental orchestrator that:
  - Enumerates all `.cs` files under each project directory and computes SHA-256 manifests
  - Diffs current vs previous manifest (added, modified, removed files)
  - On first run or `forceFullReingestion=true`: full re-parse via `ISymbolGraphBuilder`
  - On no changes: returns previous snapshot with updated `IngestionMetadata` (no build invoked)
  - On partial changes: builds only changed projects, merges preserved nodes/edges from previous snapshot
  - Deduplicates by `SymbolId` (new nodes win), deduplicates edges by `(From, To, Kind)` tuple
  - Saves `file-hashes.json` manifest atomically after successful snapshot construction
  - Produces `IngestionMetadata` with `RunId`, timestamps, `WasFullReingestion`, and per-file `FileChangeRecord`s
- Updated `IngestionService` to use `IncrementalIngestionEngine` in the parse stage (when `PipelineOverride` is null)
- Added `forceFullReingestion` parameter to `IIngestionService.IngestAsync` and `IngestionService.IngestAsync` (default `false`)
- Added `ArtifactsDir` public property to `SnapshotStore`
- Added `InternalsVisibleTo DocAgent.Tests` to `DocAgent.Ingestion.csproj`
- 6 unit tests covering: first run (full ingest), no changes (zero builds), partial re-parse (only changed project), force full re-ingestion, removed file symbol exclusion, and metadata field correctness

## Task Commits

1. **Task 1: IncrementalIngestionEngine + tests** - `13cd5a2` (feat)
2. **Task 2: Integrate into IngestionService** - `9c41161` (feat)

## Files Created/Modified

- `src/DocAgent.Ingestion/IncrementalIngestionEngine.cs` - Core incremental orchestration engine
- `src/DocAgent.Ingestion/SnapshotStore.cs` - Added `ArtifactsDir` public property
- `src/DocAgent.Ingestion/DocAgent.Ingestion.csproj` - Added `InternalsVisibleTo DocAgent.Tests`
- `src/DocAgent.McpServer/Ingestion/IngestionService.cs` - Uses engine in parse stage, loads previousSnapshot, forceFullReingestion param
- `src/DocAgent.McpServer/Ingestion/IIngestionService.cs` - Added forceFullReingestion optional param
- `tests/DocAgent.Tests/IncrementalIngestion/IncrementalIngestionEngineTests.cs` - 6 unit tests
- `tests/DocAgent.Tests/IngestionToolTests.cs` - Updated StubIngestionService signature

## Decisions Made

- RemovedFiles included in the "changed set" for project re-parse detection — a deleted `.cs` file must trigger re-parse of the owning project to remove those symbols from the snapshot
- BuildOverride hook on `IncrementalIngestionEngine` uses `(ProjectInventory, DocInputSet, CancellationToken)` signature, matching `ISymbolGraphBuilder.BuildAsync` exactly
- Manifest saved atomically AFTER snapshot construction (not before) per research Pitfall 4
- `forceFullReingestion` added as a default parameter to avoid breaking existing callers

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing] Added ArtifactsDir property to SnapshotStore**
- **Found during:** Task 2 integration
- **Issue:** `IngestionService` needed to pass `artifactsDir` to `IncrementalIngestionEngine` but `_artifactsDir` was private in `SnapshotStore`
- **Fix:** Added `public string ArtifactsDir => _artifactsDir;` property
- **Files modified:** `src/DocAgent.Ingestion/SnapshotStore.cs`

**2. [Rule 1 - Bug] Removed files must trigger project re-parse**
- **Found during:** Task 1 test `RemovedFile_SymbolsNotInOutput`
- **Issue:** Only `ChangedFiles` (added+modified) was used to determine projects needing re-parse; removed files were not included, so deleted `.cs` files did not trigger re-parse and stale symbols remained
- **Fix:** Union `diff.RemovedFiles` into the change detection set alongside `diff.ChangedFiles`
- **Files modified:** `src/DocAgent.Ingestion/IncrementalIngestionEngine.cs`

**3. [Rule 2 - Missing] InternalsVisibleTo for DocAgent.Ingestion**
- **Found during:** Task 1 — test compilation failed with CS1061 on `BuildOverride`
- **Issue:** `BuildOverride` is `internal` but `DocAgent.Tests` was not in the trust list for `DocAgent.Ingestion`
- **Fix:** Added `InternalsVisibleTo` `AssemblyAttribute` to `DocAgent.Ingestion.csproj`

## Issues Encountered

None beyond the auto-fixed deviations above.

## Next Phase Readiness

- `IncrementalIngestionEngine` is fully tested in isolation via `BuildOverride`
- Plan 03 (end-to-end test suite) can exercise the full pipeline including real Roslyn compilation
- `forceFullReingestion` escape hatch is wired and ready for MCP tool exposure in a future plan

---
*Phase: 10-incremental-ingestion*
*Completed: 2026-02-28*
