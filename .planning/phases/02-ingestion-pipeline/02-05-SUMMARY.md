---
phase: 02-ingestion-pipeline
plan: 05
subsystem: testing
tags: [determinism, integration-tests, messagepack, roslyn, xunit, msbuildworkspace]

requires:
  - phase: 02-ingestion-pipeline
    provides: RoslynSymbolGraphBuilder, SnapshotStore, LocalProjectSource, XmlDocParser, InheritDocResolver

provides:
  - End-to-end determinism tests proving byte-identical SymbolGraphSnapshot output across runs
  - 5 integration tests covering single-project, multi-project, store hashes, node sorting, edge sorting

affects: [03-indexing, all future phases consuming snapshots]

tech-stack:
  added: []
  patterns:
    - "Use `snapshot with { CreatedAt = fixedTimestamp }` to eliminate wall-clock non-determinism in tests"
    - "MessagePackSerializer.Serialize with ContractlessStandardResolver.Options for byte-level snapshot comparison"
    - "Integration test trait: [Trait(\"Category\", \"Integration\")] for slow MSBuildWorkspace tests"

key-files:
  created:
    - tests/DocAgent.Tests/DeterminismTests.cs
  modified: []

key-decisions:
  - "Fix CreatedAt via with-expression after BuildAsync rather than modifying RoslynSymbolGraphBuilder — keeps builder API minimal"
  - "Multi-project determinism test uses LocalProjectSource.DiscoverAsync against DocAgentFramework.sln to catch ordering issues across projects"

patterns-established:
  - "Byte-comparison pattern: serialize both snapshots, call bytes1.SequenceEqual(bytes2)"
  - "SnapshotStore determinism: save to separate temp dirs, assert ContentHash equality and byte-identical .msgpack files"

requirements-completed: [INGS-05]

duration: 18min
completed: 2026-02-26
---

# Phase 2 Plan 05: Determinism Test Suite Summary

**5 integration tests proving byte-identical SymbolGraphSnapshot output across independent pipeline runs, covering single-project, multi-project, SnapshotStore hash stability, and canonical node/edge ordering**

## Performance

- **Duration:** ~18 min (dominated by MSBuildWorkspace integration test execution)
- **Started:** 2026-02-26T12:00:00Z
- **Completed:** 2026-02-26T12:18:00Z
- **Tasks:** 2 of 2
- **Files modified:** 1 created

## Accomplishments
- Created DeterminismTests.cs with all 5 integration tests, all passing
- Proved byte-identical output across two independent runs of the full pipeline on DocAgent.Core
- Proved content hash stability via SnapshotStore saving to independent temp directories
- Proved multi-project determinism running against DocAgentFramework.sln (5 non-test projects)
- Verified Ordinal node sorting and (From, To, Kind) edge sorting are deterministic
- Full test suite passes: 53/53 tests green (Phase 1 + Phase 2 combined, zero failures)

## Task Commits

1. **Task 1 + 2: Create determinism tests and full suite verification** - `c859faf` (feat)

**Plan metadata:** (to be committed with SUMMARY.md)

## Files Created/Modified
- `tests/DocAgent.Tests/DeterminismTests.cs` - 5 end-to-end determinism integration tests

## Decisions Made
- Fixed `CreatedAt` using the C# `with`-expression after `BuildAsync` returns, rather than adding a timestamp parameter to the builder. This keeps the `ISymbolGraphBuilder` interface and `RoslynSymbolGraphBuilder` API minimal.
- Used separate temporary directories for each `SnapshotStore` instance in Test 2 to fully simulate independent store runs.
- Multi-project test asserts `ProjectFiles.Count > 1` before proceeding, making test intent explicit.

## Deviations from Plan

None — plan executed exactly as written. The `with { CreatedAt = fixedTimestamp }` approach was explicitly suggested in the plan's implementation notes.

## Issues Encountered

None. All 5 tests passed on the first run without any source fixes needed, confirming the existing determinism infrastructure (SymbolSorter, dictionary ordering, sorted member walks) was already correct.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

Phase 2 (Ingestion Pipeline) is now complete:
- All 5 plans executed, all 53 tests passing
- RoslynSymbolGraphBuilder produces deterministic, sorted, byte-identical snapshots
- SnapshotStore persists with stable content hashes and atomic manifest updates
- Phase 3 (Indexing) can consume SymbolGraphSnapshot artifacts from SnapshotStore

---
*Phase: 02-ingestion-pipeline*
*Completed: 2026-02-26*
