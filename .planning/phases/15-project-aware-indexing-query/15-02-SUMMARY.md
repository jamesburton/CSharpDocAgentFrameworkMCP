---
phase: 15-project-aware-indexing-query
plan: 02
subsystem: mcp-tools
tags: [mcp, project-filter, cross-project, fqn-disambiguation, csharp]

# Dependency graph
requires:
  - phase: 15-01
    provides: SearchResultItem.ProjectName, projectFilter in SearchAsync, crossProjectOnly in GetReferencesAsync
provides:
  - search_symbols MCP tool project parameter with projectFilter passthrough
  - search_symbols JSON output includes projectName per result
  - get_references MCP tool crossProjectOnly parameter with passthrough
  - get_references JSON output includes scope, fromProject, toProject per edge
  - get_symbol FQN disambiguation with multi-project conflict error
  - 4 new FQN disambiguation tests

affects: [phase-16, MCP tool consumers using search_symbols and get_references]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "FQN detection: inputs without pipe '|' treated as potential FQN; stable SymbolIds contain '|'"
    - "FQN disambiguation via ResolveByFqnAsync: search + GetSymbolAsync per candidate, group by ProjectOrigin"
    - "Edge project resolution via nodeProjectCache: Dictionary<SymbolId, string?> built from GetSymbolAsync calls"

key-files:
  created:
    - tests/DocAgent.Tests/GetSymbolFqnDisambiguationTests.cs
  modified:
    - src/DocAgent.McpServer/Tools/DocTools.cs

key-decisions:
  - "FQN heuristic: input without pipe '|' is treated as FQN candidate — stable SymbolIds always contain '|'"
  - "Same FQN in same project (multiple nodes): return first match, not ambiguous — disambiguation only across projects"
  - "nodeProjectCache resolved via GetSymbolAsync per unique From/To id (not snapshot access) — stays within IKnowledgeQueryService contract"

patterns-established:
  - "Two-phase FQN resolution: search for candidates, then check FullyQualifiedName equality per candidate node"

requirements-completed: [TOOLS-01, TOOLS-02, TOOLS-03, TOOLS-06]

# Metrics
duration: 15min
completed: 2026-03-01
---

# Phase 15 Plan 02: MCP Tool Layer Project Parameters and FQN Disambiguation Summary

**Project-aware parameters wired into MCP tools: search_symbols project filter, get_references crossProjectOnly with edge scope/project names, get_symbol FQN disambiguation — 4 new tests, 290 total passing**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-01T20:28:11Z
- **Completed:** 2026-03-01T20:45:00Z
- **Tasks:** 1
- **Files modified:** 2

## Accomplishments

- `search_symbols` now accepts optional `project` parameter (exact match, case-sensitive); passed as `projectFilter` to `SearchAsync`; JSON output includes `projectName` per result
- `get_references` now accepts optional `crossProjectOnly` parameter; passed to `GetReferencesAsync`; JSON output enriched with `scope`, `fromProject`, and `toProject` per edge
- `get_symbol` gains FQN disambiguation: inputs without `|` trigger `ResolveByFqnAsync` helper; returns error listing conflicting projects when same FQN exists in multiple projects; single-match FQN resolves transparently; no-match falls through to NotFound
- 4 new FQN disambiguation tests added (stable SymbolId direct resolve, unique FQN resolve, ambiguous FQN error, unknown FQN NotFound)
- All 290 tests pass (286 prior + 4 new)

## Task Commits

1. **Task 1: Add project parameters to MCP tools, edge project names, and FQN disambiguation** - `986b201` (feat)

## Files Created/Modified

- `src/DocAgent.McpServer/Tools/DocTools.cs` - Added project param to SearchSymbols, crossProjectOnly to GetReferences, FQN disambiguation to GetSymbol, ResolveByFqnAsync helper
- `tests/DocAgent.Tests/GetSymbolFqnDisambiguationTests.cs` - New: 4 tests for FQN disambiguation

## Decisions Made

- FQN heuristic: input without pipe `|` treated as FQN candidate (stable SymbolIds always contain `|`)
- Same FQN in same project with multiple nodes: return first match, not ambiguous — disambiguation is cross-project only
- nodeProjectCache built via `GetSymbolAsync` per unique From/To id rather than direct snapshot access, keeping DocTools within the `IKnowledgeQueryService` contract

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## Next Phase Readiness

- Phase 15 feature surface is fully complete: project-aware search, cross-project reference filtering, FQN disambiguation all wired through MCP tools
- All 290 tests pass, no regressions
- Ready for Phase 16

---

## Self-Check: PASSED

- `src/DocAgent.McpServer/Tools/DocTools.cs` - exists and modified
- `tests/DocAgent.Tests/GetSymbolFqnDisambiguationTests.cs` - exists and created
- Commit `986b201` - exists

---
*Phase: 15-project-aware-indexing-query*
*Completed: 2026-03-01*
