---
phase: 13-core-domain-extensions
plan: 01
subsystem: core-domain
tags: [csharp, dotnet, messagepack, domain-types, backward-compat]

# Dependency graph
requires: []
provides:
  - NodeKind enum (Real=0, Stub=1) on SymbolNode for stub node classification
  - EdgeScope enum (IntraProject=0, CrossProject=1, External=2) on SymbolEdge for edge scope
  - SymbolNode.ProjectOrigin optional field for multi-project source tracking
  - SymbolNode.NodeKind optional field with Real default
  - SymbolEdge.Scope optional field with IntraProject default
  - SymbolGraphSnapshot.SolutionName optional field
  - IKnowledgeQueryService.SearchAsync projectFilter optional parameter
  - 4 backward-compat MessagePack roundtrip tests proving default values
affects:
  - 13-02 (SolutionTypes uses these enums and fields)
  - 14-ingestion-extensions (populates ProjectOrigin, NodeKind, EdgeScope during ingest)
  - 15-indexing-extensions (uses projectFilter in SearchAsync implementation)
  - 16-serving-extensions (exposes projectFilter via MCP tool parameters)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Append-only optional parameters with defaults preserve backward compat on C# positional records"
    - "MessagePack ContractlessStandardResolver deserializes missing fields as default(T) — enum 0-values chosen as safe defaults"

key-files:
  created: []
  modified:
    - src/DocAgent.Core/Symbols.cs
    - src/DocAgent.Core/Abstractions.cs
    - src/DocAgent.Indexing/KnowledgeQueryService.cs
    - tests/DocAgent.Tests/SnapshotSerializationTests.cs
    - tests/DocAgent.Tests/McpToolTests.cs
    - tests/DocAgent.Tests/McpIntegrationTests.cs

key-decisions:
  - "NodeKind.Real=0 and EdgeScope.IntraProject=0 as MessagePack backward-compat defaults for old artifacts"
  - "projectFilter accepted but not yet applied in KnowledgeQueryService.SearchAsync — Phase 15 concern"
  - "SolutionName appended after IngestionMetadata on SymbolGraphSnapshot to maintain optional parameter order"

patterns-established:
  - "Extend positional records by appending optional parameters — never insert or reorder"
  - "Enum default values must be 0 for MessagePack ContractlessStandardResolver backward compat"

requirements-completed: [GRAPH-02, GRAPH-04, GRAPH-05, GRAPH-06]

# Metrics
duration: 53min
completed: 2026-03-01
---

# Phase 13 Plan 01: Core Domain Extensions Summary

**NodeKind/EdgeScope enums and extended SymbolNode/SymbolEdge/SymbolGraphSnapshot records with MessagePack backward-compat defaults and projectFilter on IKnowledgeQueryService**

## Performance

- **Duration:** 53 min
- **Started:** 2026-03-01T15:10:55Z
- **Completed:** 2026-03-01T16:03:56Z
- **Tasks:** 2 (+ 1 auto-fix deviation)
- **Files modified:** 6

## Accomplishments
- Added NodeKind and EdgeScope enums with correct 0-value defaults for MessagePack backward compat
- Extended SymbolNode with optional ProjectOrigin and NodeKind fields (append-only, no existing callers broken)
- Extended SymbolEdge with optional Scope field and SymbolGraphSnapshot with optional SolutionName field
- Added projectFilter optional parameter to IKnowledgeQueryService.SearchAsync and updated KnowledgeQueryService implementation
- Added 4 backward-compat roundtrip tests: all 9 serialization tests now pass (5 original + 4 new)
- All 236 tests pass (232 original + 4 new)

## Task Commits

1. **Task 1: Add enums and extend existing record types** - `1525477` (feat)
2. **Task 2: Extend IKnowledgeQueryService and update implementation** - `35e6085` (feat)
   - Deviation auto-fix also included: updated 3 test stub implementations for new interface signature
3. **Task 2 (tests): Add backward-compat serialization tests** - `5b40266` (test)

## Files Created/Modified
- `src/DocAgent.Core/Symbols.cs` - Added NodeKind and EdgeScope enums; extended SymbolNode, SymbolEdge, SymbolGraphSnapshot with optional fields
- `src/DocAgent.Core/Abstractions.cs` - Added projectFilter parameter to IKnowledgeQueryService.SearchAsync
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` - Updated SearchAsync signature to match interface
- `tests/DocAgent.Tests/SnapshotSerializationTests.cs` - Added 4 backward-compat MessagePack roundtrip tests
- `tests/DocAgent.Tests/McpToolTests.cs` - Updated 3 stub implementations for new SearchAsync signature
- `tests/DocAgent.Tests/McpIntegrationTests.cs` - Updated 1 stub implementation for new SearchAsync signature

## Decisions Made
- NodeKind.Real=0 and EdgeScope.IntraProject=0 chosen as enum defaults so MessagePack deserialization of old artifacts produces correct semantics
- projectFilter accepted in KnowledgeQueryService.SearchAsync but not yet applied — actual filtering is a Phase 15 concern per plan
- SolutionName appended after IngestionMetadata on SymbolGraphSnapshot to keep optional parameters at end

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated test stub implementations to match updated interface signature**
- **Found during:** Task 2 (Extend IKnowledgeQueryService)
- **Issue:** 4 test stubs implementing IKnowledgeQueryService had SearchAsync with the old signature (missing projectFilter), causing CS0535 compile errors
- **Fix:** Added `string? projectFilter = null` parameter to SearchAsync in StubKnowledgeQueryService, StaleIndexStub, InjectionDocStub (McpToolTests.cs), and StubQueryService (McpIntegrationTests.cs)
- **Files modified:** tests/DocAgent.Tests/McpToolTests.cs, tests/DocAgent.Tests/McpIntegrationTests.cs
- **Verification:** dotnet build 0 errors; all 19 McpTool tests pass
- **Committed in:** 35e6085 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - compiler error in test stubs from interface change)
**Impact on plan:** Necessary correctness fix — interface changes always require implementor updates.

## Issues Encountered
- Linter reverted file edits to SnapshotSerializationTests.cs and source files during execution, requiring re-application of changes. Resolved by re-editing and staging files immediately before committing.

## Next Phase Readiness
- Plan 01 complete. NodeKind, EdgeScope, ProjectOrigin, Scope, SolutionName fields available to all downstream layers.
- Plan 02 (SolutionTypes) already completed per git log — solution aggregate records in place.
- Phase 14 (ingestion extensions) ready to populate new fields when it begins.

---
*Phase: 13-core-domain-extensions*
*Completed: 2026-03-01*
