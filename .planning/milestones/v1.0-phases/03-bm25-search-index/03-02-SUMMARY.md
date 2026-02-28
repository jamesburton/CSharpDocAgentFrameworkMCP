---
phase: 03-bm25-search-index
plan: 02
subsystem: indexing
tags: [lucene.net, bm25, persistence, fsDirectory, freshness-check, integration-tests]

requires:
  - phase: 03-bm25-search-index
    plan: 01
    provides: BM25SearchIndex with RAMDirectory injection pattern and CamelCaseAnalyzer

provides:
  - BM25SearchIndex FSDirectory persistence at {artifactsDir}/{contentHash}.lucene/
  - IndexCommit.UserData freshness check prevents unnecessary re-indexing
  - LoadIndexAsync public method for loading persisted index without re-indexing
  - 6 integration tests covering full persistence lifecycle

affects:
  - 04-knowledge-query-service (wires BM25SearchIndex as production ISearchIndex)
  - 05-mcp-server (search_symbols tool; persistent index survives restarts)

tech-stack:
  added: []
  patterns:
    - FSDirectory.Open with per-hash subdirectory ({artifactsDir}/{contentHash}.lucene/)
    - Two-commit protocol: first commit flushes documents, second stores snapshotHash in UserData
    - SwapActiveFsDirectory disposes old directory before assigning new one
    - IsIndexFresh reads IndexCommit.UserData to check stored snapshotHash

key-files:
  created:
    - tests/DocAgent.Tests/BM25SearchIndexPersistenceTests.cs
  modified:
    - src/DocAgent.Indexing/BM25SearchIndex.cs

key-decisions:
  - "Two-commit protocol required for SetCommitData: first writer.Commit() flushes documents, then SetCommitData + Commit() stores the snapshotHash in Lucene commit metadata"
  - "IsIndexFresh wraps DirectoryReader.Open in try/catch to handle corrupted or non-Lucene directories gracefully — returns false on any exception"
  - "LoadIndexAsync throws DirectoryNotFoundException (missing) or InvalidOperationException (stale) for clear caller error handling"
  - "SwapActiveFsDirectory pattern disposes old FSDirectory before assigning new — prevents resource leak on multiple IndexAsync calls"

metrics:
  duration: 24min
  completed: 2026-02-26
  tasks: 2
  files_modified: 2
---

# Phase 3 Plan 02: BM25 Search Index Persistence Summary

**FSDirectory persistence with IndexCommit.UserData freshness check — index stored at {artifactsDir}/{contentHash}.lucene/, reloaded without re-indexing when hash matches; 6 integration tests, 66 total passing**

## Performance

- **Duration:** ~24 min
- **Started:** 2026-02-26
- **Completed:** 2026-02-26
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- BM25SearchIndex refactored to store Lucene index at `{artifactsDir}/{contentHash}.lucene/` per snapshot
- Freshness check via `IndexCommit.UserData["snapshotHash"]` skips rebuild when hash matches persisted index
- Two-commit protocol correctly stores snapshot hash in Lucene metadata after flushing documents
- `LoadIndexAsync` public method loads a persisted index without re-indexing — primary INDX-03 use case
- `SwapActiveFsDirectory` safely disposes old FSDirectory before opening new one
- 6 integration tests using unique temp directories with finally-block cleanup; all pass in 4s
- Full 66-test suite green (13m 37s due to Roslyn-heavy ingestion tests)

## Task Commits

1. **Task 1: Add FSDirectory persistence and freshness check to BM25SearchIndex** - `8b0d9ba` (feat)
2. **Task 2: Add BM25SearchIndex persistence integration tests** - `b258e72` (feat)

## Files Created/Modified

- `src/DocAgent.Indexing/BM25SearchIndex.cs` — FSDirectory persistence with per-hash subdirectory, freshness check, LoadIndexAsync, two-commit protocol, SwapActiveFsDirectory resource management
- `tests/DocAgent.Tests/BM25SearchIndexPersistenceTests.cs` — 6 integration tests: directory creation, freshness skip, separate hash directories, null hash exception, LoadIndexAsync reload, full round-trip search

## Decisions Made

- **Two-commit protocol:** `writer.Commit()` first to flush documents, then `writer.SetCommitData(...)` + `writer.Commit()` to persist the snapshotHash in Lucene's commit metadata. Single-commit approach loses data because `SetCommitData` requires a subsequent commit to persist.
- **IsIndexFresh exception handling:** Wraps `DirectoryReader.Open` in try/catch and returns false on any exception — handles corrupted directories, partial writes, and non-Lucene directories gracefully.
- **LoadIndexAsync error semantics:** Throws `DirectoryNotFoundException` for missing directories and `InvalidOperationException` for stale indexes — clear distinction allows callers to decide whether to rebuild or report error.
- **SwapActiveFsDirectory:** Disposes the old `FSDirectory` before assigning the new reference — prevents file handle leaks when `IndexAsync` is called multiple times on the same instance.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Type constructor arguments did not match actual SymbolNode/SymbolGraphSnapshot signatures**
- **Found during:** Task 2 (first test run compilation errors)
- **Issue:** Test helper used incorrect SymbolNode constructor argument order and nonexistent `SymbolKind.Class` (actual enum value is `Type`). `SymbolGraphSnapshot` used a `Guid` for `SchemaVersion` but actual type uses `string`. `SearchHit.DisplayName` does not exist — property is `Snippet`.
- **Fix:** Consulted `Symbols.cs` and `Abstractions.cs` for exact record signatures. Updated `MakeSnapshot` helper to use correct named parameters, `SymbolKind.Type`, and `SearchHit.Snippet`.
- **Files modified:** `tests/DocAgent.Tests/BM25SearchIndexPersistenceTests.cs`
- **Verification:** `dotnet test --filter BM25SearchIndexPersistenceTests` — 6/6 pass.

---

**Total deviations:** 1 auto-fixed (Rule 1 - Bug)
**Impact on plan:** No scope change; same test coverage, corrected to match actual API.

## Issues Encountered

- Full test suite takes ~14 minutes due to Roslyn workspace initialization in ingestion tests (MSBuildWorkspace is slow). The 6 new persistence tests run in ~4 seconds in isolation.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `BM25SearchIndex` ready for production wiring: pass `artifactsDir` string and call `IndexAsync` to build, or `LoadIndexAsync` to reuse a persisted index
- `IKnowledgeQueryService` implementation can delegate to `BM25SearchIndex` with lifecycle managed by artifact hash
- `InMemorySearchIndex` remains available for lightweight test doubles where persistence is unnecessary

---
*Phase: 03-bm25-search-index*
*Completed: 2026-02-26*
