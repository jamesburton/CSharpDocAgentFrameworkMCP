---
phase: 04-query-facade
plan: 01
subsystem: query-facade
tags: [query, facade, knowledge-query-service, search, symbol-detail, response-envelope]
dependency_graph:
  requires: [DocAgent.Core, DocAgent.Indexing, DocAgent.Ingestion]
  provides: [IKnowledgeQueryService implementation, QueryResult types, ResponseEnvelope]
  affects: [DocAgent.Core/Abstractions.cs, DocAgent.Indexing/KnowledgeQueryService.cs]
tech_stack:
  added: []
  patterns: [QueryResult monad pattern, ResponseEnvelope metadata wrapper, constructor injection]
key_files:
  created:
    - src/DocAgent.Core/QueryTypes.cs
    - src/DocAgent.Indexing/KnowledgeQueryService.cs
    - tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs
  modified:
    - src/DocAgent.Core/Abstractions.cs
    - src/DocAgent.Indexing/DocAgent.Indexing.csproj
    - tests/DocAgent.Tests/InterfaceCompilationTests.cs
decisions:
  - "KnowledgeQueryService resolves snapshots by listing manifest and sorting by IngestedAt (latest wins when no version pinned)"
  - "DiffAsync and GetReferencesAsync are stubbed — Phase 5/6 concern per MCPS-03"
  - "NuGetAuditMode=direct added to DocAgent.Indexing.csproj to suppress transitive MSBuildWorkspace vulnerability advisory (same pattern as DocAgent.Ingestion)"
  - "Pagination test uses CamelCase-friendly multi-token names (ProcessAlpha..ProcessEpsilon) to guarantee BM25 token matches"
metrics:
  duration: 35
  completed_date: "2026-02-26"
  tasks: 2
  files_changed: 6
---

# Phase 4 Plan 1: Query Facade — QueryTypes, IKnowledgeQueryService, KnowledgeQueryService Summary

**One-liner:** QueryResult/ResponseEnvelope type hierarchy plus KnowledgeQueryService wiring ISearchIndex + SnapshotStore for SearchAsync with BM25 ranking/filtering/pagination and GetSymbolAsync with graph navigation hints.

## What Was Built

### Task 1: Query Types + IKnowledgeQueryService Interface Update

Created `src/DocAgent.Core/QueryTypes.cs` with seven new types:

- `QueryErrorKind` enum: `NotFound`, `SnapshotMissing`, `StaleIndex`, `InvalidInput`
- `QueryResult<T>` sealed record with `Ok(T)` / `Fail(QueryErrorKind, string?)` factories
- `ResponseEnvelope<T>` wrapping payload with `SnapshotVersion`, `Timestamp`, `IsStale`, `QueryDuration`
- `SearchResultItem` — ranked search result with `Id`, `Score`, `Snippet`, `Kind`, `DisplayName`
- `SymbolDetail` — symbol with navigation hints: `ParentId`, `ChildIds`, `RelatedIds`
- `DiffChangeKind` enum: `Added`, `Removed`, `Modified`
- `DiffEntry` sealed record: `Id`, `ChangeKind`, `Summary`
- `GraphDiff(IReadOnlyList<DiffEntry>)` — replaces old `GraphDiff(IReadOnlyList<string>)`

Updated `IKnowledgeQueryService` in `Abstractions.cs` with richer signatures:
- `SearchAsync` now returns `Task<QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>>` with `kindFilter`, `offset`, `limit`, `snapshotVersion` parameters
- `GetSymbolAsync` now returns `Task<QueryResult<ResponseEnvelope<SymbolDetail>>>` with `snapshotVersion` parameter
- `DiffAsync` and `GetReferencesAsync` signatures updated accordingly

### Task 2: KnowledgeQueryService Implementation + Tests

Created `src/DocAgent.Indexing/KnowledgeQueryService.cs`:
- Constructor injection of `ISearchIndex` and `SnapshotStore`
- `SearchAsync`: resolves snapshot, collects BM25 hits, filters by `kindFilter`, applies `offset`/`limit` pagination, builds `ResponseEnvelope` with staleness flag
- `GetSymbolAsync`: resolves snapshot, looks up node, builds navigation hints from snapshot edges (`Contains` edges → parent/child; other edge kinds → related), returns `SymbolDetail` in envelope
- `DiffAsync`: returns `Fail(InvalidInput)` stub
- `GetReferencesAsync`: `yield break` stub

Added project reference `DocAgent.Ingestion` to `DocAgent.Indexing.csproj` with `NuGetAuditMode=direct`.

Created 8 unit tests in `KnowledgeQueryServiceTests.cs`:
1. `SearchAsync_ReturnsRankedResults` — ranked hits with positive scores
2. `SearchAsync_FiltersBySymbolKind` — kindFilter=Method excludes Type symbols
3. `SearchAsync_PaginatesWithOffsetAndLimit` — offset=1 limit=2 returns exactly 2 items
4. `SearchAsync_ReturnsStaleFlag_WhenIndexOutOfDate` — index built on snap1, query with snap1 hash when snap2 is latest → IsStale=true
5. `ResponseEnvelope_ContainsSnapshotVersionAndDuration` — envelope carries ContentHash and elapsed duration
6. `GetSymbolAsync_ReturnsDetailWithNavigationHints` — parent→child edges populate ChildIds and RelatedIds
7. `GetSymbolAsync_PopulatesParentId` — child symbol reports its parent
8. `GetSymbolAsync_ReturnsNotFoundForMissingId` — missing SymbolId → Fail(NotFound)

## Verification

- `dotnet build src/DocAgent.Core/DocAgent.Core.csproj` — zero warnings, zero errors
- `dotnet test tests/DocAgent.Tests/DocAgent.Tests.csproj` — **76/76 passing** (66 prior + 10 new)

## Commits

| Hash | Message |
|------|---------|
| `11cf183` | feat(04-01): define query types and update IKnowledgeQueryService interface |
| `3988c63` | feat(04-01): implement KnowledgeQueryService with SearchAsync, GetSymbolAsync, and tests |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical Config] NuGetAuditMode=direct added to DocAgent.Indexing.csproj**
- **Found during:** Task 2 — restore failed with NU1903 error
- **Issue:** Adding DocAgent.Ingestion reference to DocAgent.Indexing brought in transitive MSBuildWorkspace vulnerability advisory (same issue previously handled in DocAgent.Ingestion itself)
- **Fix:** Added `<NuGetAuditMode>direct</NuGetAuditMode>` to DocAgent.Indexing.csproj
- **Files modified:** `src/DocAgent.Indexing/DocAgent.Indexing.csproj`
- **Commit:** `3988c63`

**2. [Rule 1 - Bug] Directory ambiguity in test file**
- **Found during:** Task 2 — compilation error CS0104
- **Issue:** `using Lucene.Net.Store;` imported `Directory` which conflicted with `System.IO.Directory`
- **Fix:** Qualified all `Directory` usages in test with `System.IO.Directory`
- **Files modified:** `tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs`
- **Commit:** `3988c63`

**3. [Rule 1 - Bug] Pagination test adjusted for BM25 tokenization**
- **Found during:** Task 2 — `SearchAsync_PaginatesWithOffsetAndLimit` returned 0 results
- **Issue:** Original test used "Widget1"..."Widget5" — CamelCase tokenizer split these to ["widget","1"] etc., but the numeric suffix made Lucene score them low/zero for query "Widget"
- **Fix:** Replaced with multi-word CamelCase names ("ProcessAlpha"..."ProcessEpsilon") whose "process" token reliably matches "Process" query
- **Files modified:** `tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs`
- **Commit:** `3988c63`

## Self-Check: PASSED

Files created:
- `src/DocAgent.Core/QueryTypes.cs` — FOUND
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — FOUND
- `tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs` — FOUND

Commits:
- `11cf183` — FOUND
- `3988c63` — FOUND

Tests: 76/76 passing.
