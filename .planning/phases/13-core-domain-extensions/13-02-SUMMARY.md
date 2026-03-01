---
phase: 13-core-domain-extensions
plan: 02
subsystem: core-domain
tags: [csharp, dotnet, messagepack, records, solution-graph, domain-types]

requires:
  - phase: 13-01
    provides: Extended SymbolGraphSnapshot with SolutionName, NodeKind, EdgeScope fields

provides:
  - SolutionSnapshot sealed record aggregating per-project graphs and project DAG
  - ProjectEntry sealed record with name, path, and dependency list
  - ProjectEdge sealed record modelling directed project dependency edges
  - 6 unit tests covering construction, equality, DAG structure, and MessagePack roundtrip

affects:
  - phase-14-ingestion
  - phase-16-solution-mcp-tools

tech-stack:
  added: []
  patterns:
    - "Solution-level aggregate: SolutionSnapshot holds IReadOnlyList<SymbolGraphSnapshot> (not a merged graph)"
    - "Project DAG represented as separate ProjectEdge collection alongside ProjectEntry.DependsOn list"
    - "IReadOnlyList<string> in records uses reference equality — tests use shared list instance for Be() assertions"

key-files:
  created:
    - src/DocAgent.Core/SolutionTypes.cs
    - tests/DocAgent.Tests/SolutionSnapshotTests.cs
  modified: []

key-decisions:
  - "SolutionSnapshot holds per-project SymbolGraphSnapshots as-is (not merged) — preserves project boundaries for cross-project analysis"
  - "ProjectEntry.DependsOn and ProjectEdge collection are redundant by design — DependsOn is human-readable metadata, ProjectEdge is the machine-traversable DAG"
  - "All record fields use IReadOnlyList<T>/string/DateTimeOffset with no polymorphism — ContractlessStandardResolver serializes without attributes"

patterns-established:
  - "Record equality test with IReadOnlyList: share same list instance when testing structural record equality; use BeEquivalentTo for deep value comparison"

requirements-completed: [GRAPH-01, GRAPH-03]

duration: 28min
completed: 2026-03-01
---

# Phase 13 Plan 02: SolutionTypes Summary

**SolutionSnapshot, ProjectEntry, and ProjectEdge records added to DocAgent.Core with MessagePack roundtrip and 6 passing unit tests**

## Performance

- **Duration:** 28 min
- **Started:** 2026-03-01T14:31:55Z
- **Completed:** 2026-03-01T14:59:42Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Created `SolutionSnapshot` sealed record as solution-level aggregate holding per-project SymbolGraphSnapshots, project metadata, and project dependency DAG
- Created `ProjectEntry` and `ProjectEdge` sealed records for typed project metadata and dependency graph edges
- Added 6 unit tests covering construction, equality, multi-project scenarios, DAG structure, and MessagePack roundtrip serialization
- All 237 tests pass (231 existing + 6 new)

## Task Commits

1. **Task 1: Create SolutionTypes.cs** - `e1d3ca9` (feat)
2. **Task 2: Add SolutionSnapshotTests** - `68e35ec` (test)

## Files Created/Modified

- `src/DocAgent.Core/SolutionTypes.cs` - ProjectEntry, ProjectEdge, and SolutionSnapshot sealed records
- `tests/DocAgent.Tests/SolutionSnapshotTests.cs` - 6 unit tests for solution type construction, equality, DAG structure, and MessagePack roundtrip

## Decisions Made

- SolutionSnapshot holds per-project SymbolGraphSnapshots as separate items (not merged) to preserve project boundaries
- ProjectEdge collection and ProjectEntry.DependsOn are intentionally redundant: DependsOn is readable metadata, ProjectEdge is the machine-traversable DAG
- All types use ContractlessStandardResolver-compatible types (no custom attributes needed)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed record equality test for IReadOnlyList**
- **Found during:** Task 2 (SolutionSnapshotTests)
- **Issue:** `ProjectEntry_Construction_And_Equality` test failed because C# records use reference equality for IReadOnlyList, so two new lists with identical contents are not Be()-equal
- **Fix:** Changed test to share the same list instance for the Be() assertion; added explicit property-level assertions for completeness
- **Files modified:** tests/DocAgent.Tests/SolutionSnapshotTests.cs
- **Verification:** 6/6 tests pass, 237 total pass
- **Committed in:** 68e35ec (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - bug in test logic)
**Impact on plan:** Test logic fix only, no production code changes. No scope creep.

## Issues Encountered

- Repeated MSB3492 cache file errors from the .NET 10 preview SDK required deleting the CoreCompileInputs.cache file before first successful build
- Pre-existing compilation errors in McpToolTests.cs/McpIntegrationTests.cs from uncommitted 13-01 changes (SearchAsync signature update) were present but do not affect our files; tests ran via --no-build after project was built with working-tree changes applied

## Next Phase Readiness

- SolutionSnapshot, ProjectEntry, ProjectEdge are ready for Phase 14 (ingestion) to populate
- Phase 16 (solution MCP tools) can now reference SolutionSnapshot as the query input type
- No blockers

---
*Phase: 13-core-domain-extensions*
*Completed: 2026-03-01*
