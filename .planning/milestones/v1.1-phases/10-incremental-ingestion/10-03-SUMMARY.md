---
phase: 10-incremental-ingestion
plan: 03
subsystem: ingestion
tags: [csharp, dotnet, incremental-ingestion, correctness, integration-tests, messagepack]

requires:
  - phase: 10-02
    provides: IncrementalIngestionEngine, BuildOverride seam, IngestionMetadata

provides:
  - IncrementalCorrectnessTests: 4 integration tests proving incremental == full re-ingestion

affects: []

tech-stack:
  added: []
  patterns:
    - ContentHashedBuilder: deterministic ISymbolGraphBuilder deriving nodes from file path + SHA-256 of content
    - Normalize helper strips CreatedAt, ContentHash, IngestionMetadata before byte comparison
    - Fresh artifacts dir per "run" simulates independent executions without manifest cross-contamination
    - MessagePackSerializer.Serialize with ContractlessStandardResolver for byte comparison (matches DeterminismTests pattern)

key-files:
  created:
    - tests/DocAgent.Tests/IncrementalIngestion/IncrementalCorrectnessTests.cs
  modified: []

key-decisions:
  - "Used ContentHashedBuilder (BuildOverride fallback) instead of real Roslyn compilation — avoids MSBuild SDK resolution fragility in test context per plan fallback guidance"
  - "Fresh artifacts directory per independent run prevents manifest cross-contamination between full and incremental paths"
  - "Node ID encodes relative file path + first 8 chars of SHA-256 — stable for same content, changes when content changes, correctly models real Roslyn re-parse"

duration: 10min
completed: 2026-02-28
---

# Phase 10 Plan 03: Incremental Correctness Tests Summary

**4 integration tests prove incremental snapshot is byte-identical to full re-ingestion across all change scenarios (no-change, modification, addition, removal) using a deterministic ContentHashedBuilder**

## Performance

- **Duration:** ~10 min
- **Completed:** 2026-02-28
- **Tasks:** 1
- **Files modified:** 1

## Accomplishments

- Created `IncrementalCorrectnessTests.cs` with 4 integration tests:
  1. `Incremental_with_no_changes_produces_identical_snapshot` — verifies no-change path returns normalized byte-identical snapshot
  2. `Incremental_after_file_modification_matches_full_reingestion` — modifies one file, confirms incremental matches fresh full re-ingestion
  3. `Incremental_after_file_addition_matches_full_reingestion` — adds a new file, confirms incremental matches fresh full re-ingestion
  4. `Incremental_after_file_removal_matches_full_reingestion` — removes a file, confirms incremental matches fresh full re-ingestion
- Normalization helper strips `CreatedAt`, `ContentHash`, `IngestionMetadata` before MessagePack byte comparison (same pattern as `DeterminismTests.cs`)
- `ContentHashedBuilder`: deterministic `ISymbolGraphBuilder` that creates one `SymbolNode` per `.cs` file, with ID derived from file path + SHA-256 prefix — stable for identical content, changes when content changes
- All 222 tests pass (was 218 before this plan, +4 new tests)

## Task Commits

1. **Task 1: IncrementalCorrectnessTests** - `d74d335` (test)

## Files Created/Modified

- `tests/DocAgent.Tests/IncrementalIngestion/IncrementalCorrectnessTests.cs` — 4 correctness integration tests

## Decisions Made

- Used `ContentHashedBuilder` (BuildOverride fallback) rather than real Roslyn compilation — MSBuild SDK resolution in test context is fragile, and the plan explicitly offered this as the fallback approach. The mock exercises the actual incremental engine logic end-to-end (manifest diff, partial re-parse, symbol merge) with deterministic inputs.
- Fresh artifacts directory for each "full re-ingestion from scratch" run prevents the manifest from a prior run cross-contaminating the independent full-run baseline.
- Node ID encodes `{relPath}#{hash8}` — this models real Roslyn behavior where modified files produce different symbols (different parse output), ensuring the incremental engine's "new node wins" merge logic is exercised.

## Deviations from Plan

None — plan executed exactly as written. The `ContentHashedBuilder` fallback approach was selected as designed, and all four specified test scenarios were implemented.

## Self-Check

---
*Phase: 10-incremental-ingestion*
*Completed: 2026-02-28*
