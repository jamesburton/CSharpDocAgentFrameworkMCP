---
phase: 16-solution-mcp-tools
plan: "01"
subsystem: McpServer
tags: [mcp-tools, solution-tools, explain-solution, architecture-overview]
dependency_graph:
  requires: [DocAgent.Core.SymbolGraphSnapshot, DocAgent.Ingestion.SnapshotStore, DocAgent.McpServer.Security.PathAllowlist]
  provides: [SolutionTools.explain_solution]
  affects: [DocAgent.McpServer]
tech_stack:
  added: []
  patterns: [McpServerToolType, PathAllowlist-guard, per-project-aggregation, DAG-derivation]
key_files:
  created:
    - src/DocAgent.McpServer/Tools/SolutionTools.cs
    - tests/DocAgent.Tests/SolutionToolTests.cs
  modified: []
decisions:
  - "explain_solution derives dependency DAG from CrossProject edge scope at query time — no pre-computed adjacency stored"
  - "isSingleProject detection: unique ProjectOrigin count <= 1 (handles null ProjectOrigin by falling back to snapshot.ProjectName)"
  - "Doc coverage counts only public/protected/protectedInternal nodes of kinds: Type, Method, Property, Constructor, Delegate, Event, Field"
  - "Edge count per project: IntraProject edges where From or To belongs to that project's node set (not exclusive containment)"
  - "Stub nodes excluded from project stats — counted globally as totalStubNodeCount only"
  - "ErrorResponse always returns opaque 'Solution not found.' regardless of VerboseErrors for allowlist denials"
requirements_completed: [TOOLS-05]
metrics:
  duration: "~15 minutes"
  completed: "2026-03-02"
  tasks_completed: 2
  files_created: 2
  tests_added: 7
---

# Phase 16 Plan 01: SolutionTools explain_solution Summary

**One-liner:** explain_solution MCP tool with per-project node/edge counts, doc coverage %, cross-project DAG, stub count, and single-project detection from flat SymbolGraphSnapshot.

## What Was Built

Created `SolutionTools` class in `src/DocAgent.McpServer/Tools/SolutionTools.cs` with the `explain_solution` MCP tool. Follows the exact ChangeTools pattern (McpServerToolType, PathAllowlist guard, JsonSerializerOptions, ErrorResponse helper).

The tool computes:
- **Per-project stats**: groups Real nodes by ProjectOrigin, counts IntraProject edges touching each project, computes doc coverage for public/protected/protectedInternal doc-relevant kinds
- **Dependency DAG**: derives adjacency list from CrossProject edges, mapping fromProject → [toProject, ...] unique sorted pairs
- **Stub count**: global count of NodeKind.Stub nodes across all projects
- **isSingleProject**: true when only one unique ProjectOrigin exists in the Real nodes
- **PathAllowlist security**: opaque "Solution not found." denial when snapshot store directory is blocked

JSON response shape:
```json
{
  "solutionName": "MySolution",
  "snapshotId": "abc123",
  "projects": [{"name": "...", "nodeCount": N, "edgeCount": N, "docCoveragePercent": N.N}],
  "dependencyDag": {"ProjectA": ["ProjectB"]},
  "totalStubNodeCount": N,
  "isSingleProject": false
}
```

## Tasks Completed

| Task | Description | Commit | Files |
|------|-------------|--------|-------|
| 1 | SolutionTools.cs with explain_solution | 36162e5 | src/DocAgent.McpServer/Tools/SolutionTools.cs |
| 2 | SolutionToolTests.cs (7 tests) | 57594ab | tests/DocAgent.Tests/SolutionToolTests.cs |

## Deviations from Plan

None — plan executed exactly as written.

## Verification

- `dotnet build src/DocAgent.McpServer`: PASSED — 0 warnings, 0 errors
- `dotnet test --filter "FullyQualifiedName~SolutionToolTests"`: PASSED — 7/7 tests
- `dotnet test --filter "FullyQualifiedName~SolutionToolTests|FullyQualifiedName~ChangeToolTests|FullyQualifiedName~DocToolTests|FullyQualifiedName~SearchToolTests"`: PASSED — 20/20 tests (7 new + 13 existing MCP tool tests)

## Self-Check: PASSED

- [x] src/DocAgent.McpServer/Tools/SolutionTools.cs exists
- [x] tests/DocAgent.Tests/SolutionToolTests.cs exists
- [x] Commit 36162e5 exists (SolutionTools.cs)
- [x] Commit 57594ab exists (SolutionToolTests.cs)
- [x] All 7 SolutionToolTests pass
