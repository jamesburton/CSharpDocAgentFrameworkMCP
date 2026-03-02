---
phase: 16-solution-mcp-tools
plan: "02"
subsystem: McpServer
tags: [mcp-tools, solution-tools, diff-snapshots, symbol-graph-differ]
dependency_graph:
  requires:
    - phase: 16-01
      provides: SolutionTools class with explain_solution, PathAllowlist guard, ErrorResponse helper
    - phase: DocAgent.Core.SymbolGraphDiffer
      provides: SymbolGraphDiffer.Diff static method for per-project snapshot comparison
  provides:
    - SolutionTools.diff_snapshots MCP tool
    - Per-project symbol diff using reconstructed per-project snapshots from flat solution snapshot
    - Projects added/removed detection between snapshot versions
    - Cross-project edge change reporting with project attribution
  affects: [DocAgent.McpServer]
tech-stack:
  added: []
  patterns: [ExtractProjectSnapshot-helper, GroupByProject-aggregation, CrossProjectEdge-set-diff, FormatEdgeEndpoint-attribution]
key-files:
  created: []
  modified:
    - src/DocAgent.McpServer/Tools/SolutionTools.cs
    - tests/DocAgent.Tests/SolutionToolTests.cs
key-decisions:
  - "ExtractProjectSnapshot reconstructs per-project snapshots by filtering IntraProject edges where From or To belongs to project node set (same inclusion rule as explain_solution edge counting)"
  - "Cross-project edge equality keyed on (From.Value, To.Value, Kind) tuple — Scope excluded since all cross-project edges share the same scope"
  - "FormatEdgeEndpoint formats as Project::SymbolId for attributed attribution; falls back to bare symbolId if node not found in map"
  - "projectDiffs includes all surviving projects even those with zero changes — omitting zero-change projects would confuse agents comparing membership"
patterns-established:
  - "GroupByProject: filter Real nodes, group by ProjectOrigin ?? snapshot.ProjectName — consistent with explain_solution"
  - "ExtractProjectSnapshot: use record with-expression to derive per-project snapshot; filter edges to IntraProject scope with From/To membership check"
requirements-completed: [TOOLS-04]
duration: ~15min
completed: 2026-03-02
---

# Phase 16 Plan 02: SolutionTools diff_snapshots Summary

**diff_snapshots MCP tool added to SolutionTools: per-project symbol diffs via SymbolGraphDiffer, projects added/removed detection, and cross-project edge attribution from flat SymbolGraphSnapshot pairs.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-02T00:00:00Z
- **Completed:** 2026-03-02T00:15:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Added `diff_snapshots` MCP tool to the existing `SolutionTools` class with correct `[McpServerTool]` attributes and PathAllowlist guard
- Per-project diffs reconstruct per-project `SymbolGraphSnapshot` objects from the flat solution snapshot using `ExtractProjectSnapshot` helper, then call `SymbolGraphDiffer.Diff` — projects added/removed detected and reported in dedicated JSON sections
- Cross-project edge changes computed via `(From.Value, To.Value, Kind)` set difference with project-attributed `Project::SymbolId` formatting
- 6 new unit tests covering all required behaviors; full suite at 293/293 passing

## Task Commits

1. **Task 1: Add diff_snapshots tool to SolutionTools** - `1e14d7a` (feat)
2. **Task 2: Unit tests for diff_snapshots** - `cfc1f85` (test)

## Files Created/Modified

- `src/DocAgent.McpServer/Tools/SolutionTools.cs` - Added DiffSnapshots method + 4 private helper methods (GroupByProject, ExtractProjectSnapshot, BuildNodeProjectMap, FormatEdgeEndpoint)
- `tests/DocAgent.Tests/SolutionToolTests.cs` - Added 6 new diff_snapshots tests (Tests 8-13)

## Decisions Made

- ExtractProjectSnapshot filters edges to IntraProject scope with From-or-To membership — same inclusive rule as explain_solution edge counting, ensuring consistency
- Cross-project edge equality keyed on (From.Value, To.Value, Kind) — Scope excluded since by definition all are CrossProject
- projectDiffs includes all surviving projects (even zero-change) for complete membership information

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None — build and all tests passed on first attempt.

## Next Phase Readiness

- Phase 16 diff_snapshots complete; SolutionTools now has both explain_solution and diff_snapshots tools
- Phase 17 (if any) can reference SolutionTools as the complete solution-level MCP tool surface

## Verification

- `dotnet build src/DocAgent.McpServer`: PASSED — 0 warnings, 0 errors
- `dotnet test --filter "FullyQualifiedName~SolutionToolTests"`: PASSED — 13/13 (7 original + 6 new)
- `dotnet test`: PASSED — 293/293 total

## Self-Check: PASSED

- [x] src/DocAgent.McpServer/Tools/SolutionTools.cs modified (DiffSnapshots method present)
- [x] tests/DocAgent.Tests/SolutionToolTests.cs modified (6 new diff_snapshots tests)
- [x] Commit 1e14d7a exists (diff_snapshots tool)
- [x] Commit cfc1f85 exists (diff_snapshots tests)
- [x] All 13 SolutionToolTests pass
- [x] 293/293 total tests pass

---
*Phase: 16-solution-mcp-tools*
*Completed: 2026-03-02*
