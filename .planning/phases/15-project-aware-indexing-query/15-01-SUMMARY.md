---
phase: 15-project-aware-indexing-query
plan: 01
subsystem: indexing
tags: [lucene, bm25, search, project-filter, cross-project, csharp]

# Dependency graph
requires:
  - phase: 14.1-solution-graph-enrichment
    provides: SymbolNode.ProjectOrigin, EdgeScope enum, NodeKind filtering in BM25/InMemory indexes
provides:
  - SearchResultItem.ProjectName property (null-default, backward-compat)
  - IKnowledgeQueryService.GetReferencesAsync crossProjectOnly parameter
  - KnowledgeQueryService.SearchAsync projectFilter application
  - BM25SearchIndex stores projectName as Lucene StringField
  - 9 new tests covering project-aware search and cross-project edge filtering
affects: [phase-15-02, MCP tool layer serving search_symbols and get_references]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "projectFilter applied at service layer (not index layer) — locked design decision"
    - "crossProjectOnly uses EdgeScope enum comparison at GetReferencesAsync iteration"
    - "Positional record with nullable trailing parameter preserves backward compat"

key-files:
  created:
    - tests/DocAgent.Tests/ProjectAwareIndexingTests.cs
    - tests/DocAgent.Tests/CrossProjectQueryTests.cs
  modified:
    - src/DocAgent.Core/QueryTypes.cs
    - src/DocAgent.Core/Abstractions.cs
    - src/DocAgent.Indexing/KnowledgeQueryService.cs
    - src/DocAgent.Indexing/BM25SearchIndex.cs
    - src/DocAgent.McpServer/Tools/DocTools.cs
    - tests/DocAgent.Tests/McpIntegrationTests.cs
    - tests/DocAgent.Tests/McpToolTests.cs

key-decisions:
  - "projectFilter at service layer only (not ISearchIndex) — preserves locked design constraint"
  - "crossProjectOnly uses EdgeScope.CrossProject enum value, exact match, no partial scoping"
  - "SearchResultItem.ProjectName = null for legacy nodes with ProjectOrigin=null (backward compat)"

patterns-established:
  - "Interface parameter added with default value preserves all existing callers without change"
  - "Test stubs implementing IKnowledgeQueryService need bool crossProjectOnly = false in GetReferencesAsync"

requirements-completed: [TOOLS-01, TOOLS-03, TOOLS-06]

# Metrics
duration: 15min
completed: 2026-03-01
---

# Phase 15 Plan 01: Project-Aware Indexing and Cross-Project Query Summary

**Project attribution wired through indexing and query layers: SearchResultItem.ProjectName, projectFilter in SearchAsync, crossProjectOnly in GetReferencesAsync, and projectName Lucene field — 9 new tests, 286 total passing**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-01T20:10:00Z
- **Completed:** 2026-03-01T20:25:55Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- Added `ProjectName` (nullable, default null) to `SearchResultItem` positional record — backward compat preserved for all existing callers
- `KnowledgeQueryService.SearchAsync` now applies `projectFilter` and populates `ProjectName` from `node.ProjectOrigin`
- `GetReferencesAsync` extended with `crossProjectOnly = false` parameter; filters `EdgeScope.CrossProject` edges when true
- `BM25SearchIndex.WriteDocuments` now stores `projectName` as a `StringField` in each Lucene document
- 9 new tests (5 ProjectAwareIndexingTests + 4 CrossProjectQueryTests) all green; full suite 286/286 pass

## Task Commits

1. **Task 1: Extend core types and service layer** - `58a5b18` (feat)
2. **Task 2: Add project-aware indexing and cross-project query tests** - `28db4b9` (feat)

**Plan metadata:** _(final docs commit follows)_

## Files Created/Modified
- `src/DocAgent.Core/QueryTypes.cs` - Added `string? ProjectName = null` to SearchResultItem record
- `src/DocAgent.Core/Abstractions.cs` - Added `bool crossProjectOnly = false` to IKnowledgeQueryService.GetReferencesAsync
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` - projectFilter applied in SearchAsync, crossProjectOnly in GetReferencesAsync
- `src/DocAgent.Indexing/BM25SearchIndex.cs` - projectName StringField added to WriteDocuments
- `src/DocAgent.McpServer/Tools/DocTools.cs` - Fixed GetReferencesAsync call to use named `ct:` parameter
- `tests/DocAgent.Tests/McpIntegrationTests.cs` - StubQueryService GetReferencesAsync updated with crossProjectOnly param
- `tests/DocAgent.Tests/McpToolTests.cs` - Three stub classes updated with crossProjectOnly param
- `tests/DocAgent.Tests/ProjectAwareIndexingTests.cs` - New: 5 tests for project-aware search
- `tests/DocAgent.Tests/CrossProjectQueryTests.cs` - New: 4 tests for cross-project edge filtering

## Decisions Made
- projectFilter remains at service layer only (not pushed to ISearchIndex) — locked design constraint from prior phases
- Exact case-sensitive match for projectFilter — locked decision
- crossProjectOnly uses EdgeScope.CrossProject enum value; IntraProject and External edges are excluded when true

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated existing IKnowledgeQueryService stub callers to match new interface signature**
- **Found during:** Task 1 (build verification after interface change)
- **Issue:** DocTools.cs, McpIntegrationTests.cs, and McpToolTests.cs (3 stubs) all implemented the old GetReferencesAsync without crossProjectOnly — compiler errors CS0535 and CS1503
- **Fix:** Added `bool crossProjectOnly = false` to all stub implementations; changed DocTools.cs positional call to named `ct:` parameter
- **Files modified:** src/DocAgent.McpServer/Tools/DocTools.cs, tests/DocAgent.Tests/McpIntegrationTests.cs, tests/DocAgent.Tests/McpToolTests.cs
- **Verification:** `dotnet build` succeeds with zero errors
- **Committed in:** 58a5b18 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 3 blocking)
**Impact on plan:** Required for compiler correctness — adding optional parameter to interface requires all implementations to be updated. No scope creep.

## Issues Encountered
None beyond the interface implementation update handled automatically.

## Next Phase Readiness
- Project-aware search and cross-project reference filtering are fully functional in the service and indexing layers
- MCP tool layer (search_symbols, get_references) can now expose projectFilter and crossProjectOnly to callers in Phase 15 Plan 02
- All 286 tests pass, no regressions

---
*Phase: 15-project-aware-indexing-query*
*Completed: 2026-03-01*
