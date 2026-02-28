---
phase: 06-analysis-hosting
plan: "04"
subsystem: integration-gaps
tags: [bug-fix, interface, forceReindex, env-var, tech-debt]
dependency_graph:
  requires: []
  provides: [MISSING-01-fix, MISSING-02-fix, ISearchIndex-downcast-removal]
  affects: [AppHost, BM25SearchIndex, IngestionService, McpServer]
tech_stack:
  added: []
  patterns: [optional-parameter-default, interface-contract-extension]
key_files:
  created: []
  modified:
    - src/DocAgent.AppHost/Program.cs
    - src/DocAgent.Core/Abstractions.cs
    - src/DocAgent.Indexing/BM25SearchIndex.cs
    - src/DocAgent.Indexing/InMemorySearchIndex.cs
    - src/DocAgent.McpServer/Ingestion/IngestionService.cs
    - src/DocAgent.McpServer/Program.cs
    - tests/DocAgent.Tests/IngestionServiceTests.cs
key_decisions:
  - "forceReindex added as optional param (default false) to ISearchIndex.IndexAsync — backwards-compatible with all existing callers"
  - "BM25SearchIndex freshness check guarded by !forceReindex to allow forced rebuild"
  - "using DocAgent.Indexing retained in McpServer/Program.cs for SnapshotStore reference"
metrics:
  duration: "12 min"
  completed: "2026-02-28"
  tasks: 4
  files: 7
---

# Phase 06 Plan 04: Integration Gap Fixes Summary

**One-liner:** Three targeted fixes — AppHost env var name corrected, forceReindex wired through the index pipeline, and ISearchIndex downcast eliminated from McpServer startup.

## What Was Built

Closed two v1.0 audit integration gaps and one tech debt item:

1. **MISSING-01 (AppHost env var):** `DOCAGENT_ALLOWLIST_PATHS` corrected to `DOCAGENT_ALLOWED_PATHS` in `DocAgent.AppHost/Program.cs`. The allowlist was silently ignored under Aspire because `PathAllowlist.cs` reads a different variable name.

2. **MISSING-02 (forceReindex no-op):** `forceReindex` parameter was accepted by `IngestionService.IngestAsync` but never used. Fixed by:
   - Adding `bool forceReindex = false` to `ISearchIndex.IndexAsync` (default keeps all callers compiling)
   - `BM25SearchIndex.IndexAsync` now checks `!forceReindex && IsIndexFresh(...)` — forced rebuild bypasses the hash-based freshness guard
   - `IngestionService` passes `forceReindex` through to `_index.IndexAsync`
   - Two new tests confirm `forceReindex=true` and `forceReindex=false` are forwarded correctly

3. **Tech debt (downcast removal):** `McpServer/Program.cs` line 80 cast `(BM25SearchIndex)` removed — `IndexAsync` is on the `ISearchIndex` interface, no concrete type needed.

## Verification

- `DOCAGENT_ALLOWED_PATHS` now appears in both AppHost/Program.cs and (verified via grep) PathAllowlist.cs
- `DOCAGENT_ALLOWLIST_PATHS` no longer appears in any source file
- `Program.cs` contains `GetRequiredService<ISearchIndex>()` with no cast
- 144/144 unit tests pass; 0 build warnings

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing implementation] Updated InMemorySearchIndex signature**
- **Found during:** Task 2 — interface change required all implementations to match
- **Issue:** `InMemorySearchIndex.IndexAsync` had old 2-param signature, would not compile against updated interface
- **Fix:** Added `bool forceReindex = false` parameter (ignored in-memory implementation)
- **Files modified:** `src/DocAgent.Indexing/InMemorySearchIndex.cs`
- **Commit:** 833f864

**2. [Rule 2 - Missing implementation] Updated test stubs to match new interface**
- **Found during:** Task 2 — `StubSearchIndex` and `FailingSearchIndex` in IngestionServiceTests.cs had old signature
- **Fix:** Updated both stubs; `StubSearchIndex` now tracks `IndexCallCount` and `LastForceReindex` for the new tests
- **Files modified:** `tests/DocAgent.Tests/IngestionServiceTests.cs`
- **Commit:** 833f864

## Self-Check: PASSED

Files confirmed present:
- src/DocAgent.AppHost/Program.cs — contains DOCAGENT_ALLOWED_PATHS
- src/DocAgent.Core/Abstractions.cs — contains forceReindex = false
- src/DocAgent.McpServer/Ingestion/IngestionService.cs — contains forceReindex
- src/DocAgent.McpServer/Program.cs — contains GetRequiredService<ISearchIndex>()

Commits confirmed:
- 591136d — fix(06-04): correct AppHost env var
- 833f864 — feat(06-04): implement forceReindex support
- 580b689 — refactor(06-04): remove BM25SearchIndex downcast
- 824708c — test(06-04): verify full test suite passes
