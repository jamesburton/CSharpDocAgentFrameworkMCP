---
phase: 12-changetools-security-gate
plan: "01"
subsystem: McpServer/Security
tags: [security, path-allowlist, change-tools, mcp-tools]
dependency_graph:
  requires: [11-02]
  provides: [R-CHANGE-TOOLS]
  affects: [src/DocAgent.McpServer/Tools/ChangeTools.cs]
tech_stack:
  added: []
  patterns: [PathAllowlist-enforcement, opaque-denial-pattern]
key_files:
  created: []
  modified:
    - src/DocAgent.McpServer/Tools/ChangeTools.cs
    - tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs
decisions:
  - "PathAllowlist guard checks _snapshotStore.ArtifactsDir once per method, not once per LoadAsync call"
  - "Uses QueryErrorKind.NotFound for opaque denial matching DocTools pattern"
  - "ExplainChange test uses SaveBreakingPairAsync symbol ID — guard fires before load so symbol existence is irrelevant"
metrics:
  duration_minutes: 15
  completed_date: "2026-03-01"
  tasks_completed: 2
  files_modified: 2
---

# Phase 12 Plan 01: ChangeTools PathAllowlist Security Gate Summary

**One-liner:** PathAllowlist enforcement added to all 3 ChangeTools MCP methods (ReviewChanges, FindBreakingChanges, ExplainChange) with opaque not_found denial matching DocTools security pattern.

## What Was Built

Closed the security gap identified in the v1.1 milestone audit: ChangeTools received a `PathAllowlist` via DI but never called `IsAllowed()`. All three tool methods now enforce the allowlist before any snapshot loading.

### Guard block pattern (applied to all 3 methods):
```csharp
// PathAllowlist gate — deny access to snapshot store if directory is not allowed
if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
{
    _logger.LogWarning("ChangeTools: snapshot store directory denied by allowlist");
    activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
    return ErrorResponse(QueryErrorKind.NotFound, "Access denied.");
}
```

### Extended `CreateTools` helper in tests:
```csharp
private ChangeTools CreateTools(SnapshotStore? store = null, PathAllowlist? allowlist = null)
```

### 3 new denial unit tests added:
- `ReviewChanges_PathDenied_ReturnsAccessDenied`
- `FindBreakingChanges_PathDenied_ReturnsAccessDenied`
- `ExplainChange_PathDenied_ReturnsAccessDenied`

All use `new PathAllowlist(Options.Create(new DocAgentServerOptions()))` (no AllowedPaths = deny temp dirs).

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | 333d37f | feat(12-01): add PathAllowlist guard blocks to all 3 ChangeTools methods |
| 2 | eebfbf6 | test(12-01): add PathAllowlist denial unit tests for all 3 ChangeTools methods |

## Verification

- `grep -c "_allowlist.IsAllowed" src/DocAgent.McpServer/Tools/ChangeTools.cs` → **3**
- `dotnet build src/DocAgentFramework.sln` → **0 errors, 0 warnings**
- `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` → **13/13 passed** (10 existing + 3 new)

## Deviations from Plan

None - plan executed exactly as written.

## Self-Check: PASSED

- [x] `src/DocAgent.McpServer/Tools/ChangeTools.cs` — modified with 3 guard blocks
- [x] `tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs` — extended helper + 3 new tests
- [x] Commit 333d37f exists
- [x] Commit eebfbf6 exists
- [x] All 13 ChangeToolTests pass
