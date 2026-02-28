---
phase: 01-core-domain
plan: 03
subsystem: core-domain
tags: [interfaces, async-enumerable, contracts, compile-time-tests]
dependency_graph:
  requires: [01-01, 01-02]
  provides: [stable-interface-contracts, IAsyncEnumerable-search, IVectorIndex-stub, SearchToListAsync-extension]
  affects: [phase-02-ingestion, phase-03-indexing, phase-04-query]
tech_stack:
  added: []
  patterns: [IAsyncEnumerable, EnumeratorCancellation, compile-time-contract-tests, extension-methods]
key_files:
  created:
    - path: tests/DocAgent.Tests/InterfaceCompilationTests.cs
      description: CORE-03 compile-time contract verification for all six domain interfaces
  modified:
    - path: src/DocAgent.Core/Abstractions.cs
      description: ISearchIndex and IKnowledgeQueryService switched to IAsyncEnumerable; IVectorIndex stub added; SearchToListAsync extension added
    - path: src/DocAgent.Indexing/InMemorySearchIndex.cs
      description: SearchAsync updated to async IAsyncEnumerable<SearchHit>; null-coalescing fix for DisplayName
    - path: tests/DocAgent.Tests/InMemorySearchIndexTests.cs
      description: Updated to use SearchToListAsync extension method
decisions:
  - "[EnumeratorCancellation] attribute not valid on interface declarations — removed from interface signatures, valid only on async-iterator implementations"
  - "IVectorIndex left as empty stub per VCTR-01 — embeddings/vector search is V2 concern"
  - "SearchToListAsync uses manual await foreach loop to avoid potential ambiguity with System.Linq.Async if pulled in transitively"
metrics:
  duration_seconds: 480
  completed_date: 2026-02-26
  tasks_completed: 2
  tasks_total: 2
  files_created: 1
  files_modified: 3
---

# Phase 1 Plan 03: Interface Contract Finalization Summary

**One-liner:** Finalized all six domain interface contracts with IAsyncEnumerable search signatures, GetReferencesAsync, IVectorIndex stub, and SearchToListAsync extension; updated InMemorySearchIndex and added compile-time contract verification tests.

## What Was Built

### Task 1: Expand interface contracts in Abstractions.cs

`src/DocAgent.Core/Abstractions.cs` updated in-place with all six domain interfaces locked:

- **ISearchIndex** — `SearchAsync` changed from `Task<IReadOnlyList<SearchHit>>` to `IAsyncEnumerable<SearchHit>`
- **IKnowledgeQueryService** — `SearchAsync` changed to `IAsyncEnumerable<SearchHit>`; `GetReferencesAsync(SymbolId, CancellationToken)` added returning `IAsyncEnumerable<SymbolEdge>`
- **IVectorIndex** — new empty stub interface per VCTR-01 (V2 vector search placeholder)
- **SearchIndexExtensions** — `SearchToListAsync` convenience extension using manual `await foreach` loop

### Task 2: Update InMemorySearchIndex and add InterfaceCompilationTests

- **InMemorySearchIndex.cs** — `SearchAsync` changed to `async IAsyncEnumerable<SearchHit>` using `yield return` pattern; added `using System.Runtime.CompilerServices` for `[EnumeratorCancellation]`; fixed null reference for `DisplayName` with `?? string.Empty`
- **InMemorySearchIndexTests.cs** — updated to call `SearchToListAsync` extension instead of direct `SearchAsync`
- **InterfaceCompilationTests.cs** (new) — three tests:
  1. `All_six_interfaces_are_assignable` — compile-time check that all six interfaces (IProjectSource, IDocSource, ISymbolGraphBuilder, ISearchIndex, IKnowledgeQueryService, IVectorIndex) are referenceable
  2. `SearchToListAsync_extension_method_exists` — reflection-based verification of the extension method and return type
  3. `IKnowledgeQueryService_has_GetReferencesAsync` — reflection-based verification of GetReferencesAsync with correct IAsyncEnumerable<SymbolEdge> return type

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Removed [EnumeratorCancellation] from interface declarations**
- **Found during:** Task 1 (build verification)
- **Issue:** Plan specified `[EnumeratorCancellation]` on interface method parameters, but the compiler emits CS8424 — `[EnumeratorCancellation]` is only valid on async-iterator method implementations, not interface declarations.
- **Fix:** Removed the attribute from all three interface method signatures. Attribute remains valid (and was applied) on the InMemorySearchIndex implementation.
- **Files modified:** src/DocAgent.Core/Abstractions.cs
- **Commit:** 27d2bd7

**2. [Rule 1 - Bug] Null-coalescing fix for DisplayName in SearchHit**
- **Found during:** Task 2 (compilation of InMemorySearchIndex)
- **Issue:** `SymbolNode.DisplayName` is nullable (`string?`) but `SearchHit.Snippet` constructor parameter requires non-null `string`. TreatWarningsAsErrors=true escalated this to an error.
- **Fix:** Changed `new SearchHit(n.Id, 1.0, n.DisplayName)` to `new SearchHit(n.Id, 1.0, n.DisplayName ?? string.Empty)`.
- **Files modified:** src/DocAgent.Indexing/InMemorySearchIndex.cs
- **Commit:** e3fed34

### Deferred Issues (Out of Scope)

**SnapshotSerializationTests.Json_roundtrip_produces_equivalent_snapshot (pre-existing failure)**
- This test file was added in plan 01-02 but is not yet committed (untracked). The Json roundtrip test fails due to MessagePack deserialization producing a different object graph than the original. This is pre-existing and unrelated to plan 01-03 changes. Logged for plan 01-02 follow-up.

## Verification Results

```
dotnet build src/DocAgent.Core/DocAgent.Core.csproj
  Build succeeded. 0 Errors. 0 Warnings.

dotnet test tests/DocAgent.Tests --filter "InMemorySearchIndexTests|InterfaceCompilationTests|SymbolIdTests"
  Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9

Grep for IVectorIndex in Abstractions.cs: FOUND
Grep for GetReferencesAsync in Abstractions.cs: FOUND
Grep for IAsyncEnumerable<SearchHit> in Abstractions.cs: FOUND (x2 — ISearchIndex, IKnowledgeQueryService)
```

## Self-Check: PASSED

All created/modified files verified on disk. All commits verified in git history.
- src/DocAgent.Core/Abstractions.cs: FOUND
- src/DocAgent.Indexing/InMemorySearchIndex.cs: FOUND
- tests/DocAgent.Tests/InMemorySearchIndexTests.cs: FOUND
- tests/DocAgent.Tests/InterfaceCompilationTests.cs: FOUND
- Commit 27d2bd7 (Task 1): FOUND
- Commit e3fed34 (Task 2): FOUND
