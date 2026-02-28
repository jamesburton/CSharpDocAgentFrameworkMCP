---
phase: 02-ingestion-pipeline
plan: "03"
subsystem: ingestion
tags: [roslyn, symbol-graph, msbuild, determinism, integration-tests]
dependency_graph:
  requires: [02-01, 02-02]
  provides: [ISymbolGraphBuilder implementation, SymbolSorter]
  affects: [DocAgent.Ingestion, DocAgent.Tests]
tech_stack:
  added: []
  patterns:
    - MSBuildWorkspace per-project open with disposal for memory release
    - CoreSymbolKind alias to disambiguate from Microsoft.CodeAnalysis.SymbolKind
    - Roslyn GetDocumentationCommentId() as SymbolId value
    - 0-based Roslyn line positions converted to 1-based SourceSpan
key_files:
  created:
    - src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs
    - src/DocAgent.Ingestion/SymbolSorter.cs
    - tests/DocAgent.Tests/RoslynSymbolGraphBuilderTests.cs
  modified: []
decisions:
  - "CoreSymbolKind alias required — DocAgent.Core.SymbolKind conflicts with Microsoft.CodeAnalysis.SymbolKind in the Ingestion namespace"
  - "MSBuildWorkspace created and disposed per project inside ProcessProjectAsync to bound Roslyn compilation memory"
  - "SymbolId value uses GetDocumentationCommentId() falling back to ToDisplayString() when null (rare for top-level symbols)"
  - "Accessibility.ProtectedOrInternal (Roslyn) mapped to ProtectedInternal and included in filter to expose protected-internal surface"
metrics:
  duration: "~25 min"
  completed: "2026-02-26"
  tasks_completed: 2
  files_created: 3
  files_modified: 0
---

# Phase 2 Plan 3: RoslynSymbolGraphBuilder Summary

**One-liner:** Roslyn-backed symbol graph builder that walks compilations recursively, extracting typed nodes and semantic edges with XML doc parsing and deterministic SymbolSorter ordering.

## What Was Built

### RoslynSymbolGraphBuilder (ISymbolGraphBuilder)

Full recursive Roslyn symbol walker that transforms `ProjectInventory` + `DocInputSet` → `SymbolGraphSnapshot`:

- **Per-project MSBuildWorkspace** — opens one project at a time, nulls compilation reference after walking to release memory (avoids unbounded retention per research pitfall 4)
- **Recursive namespace walker** — `WalkNamespace` → `WalkType` → member iteration, all ordered by `GetDocumentationCommentId()` for determinism
- **Accessibility filter** — public, protected, and protected-internal only; private and internal excluded
- **Edge extraction:**
  - `Contains` — namespace → type, type → member, type → nested type
  - `Inherits` — type → base class (excluding System.Object)
  - `Implements` — type → interface
  - `References` — member → parameter types, return types (unwraps arrays and generic args, skips special types)
  - `Overrides` — method → overridden method
- **Doc resolution chain:** `XmlDocParser.Parse()` → `InheritDocResolver.Resolve()` → synthesized placeholder `DocComment("No documentation provided.")`
- **Generated code detection** — `[GeneratedCode]` attribute or `/obj/` path check
- **SourceSpan** — extracted from `symbol.Locations.FirstOrDefault(l => l.IsInSource)`, 0-based Roslyn positions converted to 1-based

### SymbolSorter

Static utility for deterministic snapshot ordering:
- `SortNodes` — `OrderBy(n => n.Id.Value, StringComparer.Ordinal)`
- `SortEdges` — `OrderBy(From).ThenBy(To).ThenBy(Kind)` (all Ordinal)

### Integration Tests (8 tests)

All tests use `[Trait("Category", "Integration")]` and run against this repo's `DocAgent.Core.csproj`:

1. `BuildAsync_produces_nodes_for_core_project` — SymbolId, SymbolNode, SymbolEdge, SymbolGraphSnapshot, IProjectSource, ISearchIndex all present
2. `BuildAsync_creates_containment_edges` — Contains edges exist and target valid node IDs
3. `BuildAsync_creates_inheritance_edges` — edge builder doesn't crash; self-referential edges absent
4. `BuildAsync_excludes_private_members` — no Private or Internal accessibility in snapshot
5. `BuildAsync_includes_doc_comments` — non-null Summary present; every node has non-null Docs
6. `BuildAsync_assigns_source_spans` — span nodes have 1-based line/col and non-null FilePath
7. `BuildAsync_nodes_are_sorted` — Ordinal ordering verified across full list
8. `BuildAsync_edges_are_sorted` — canonical (From, To, Kind) order verified across full list

**All 48 tests in the suite pass.**

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] SymbolKind namespace ambiguity**
- **Found during:** Task 1 (first build)
- **Issue:** `DocAgent.Core.SymbolKind` and `Microsoft.CodeAnalysis.SymbolKind` both visible in `DocAgent.Ingestion` namespace; compiler error CS0104
- **Fix:** Added `using CoreSymbolKind = DocAgent.Core.SymbolKind;` alias, replaced all `SymbolKind.X` references in `MapKind` and `CreateNamespaceNode` with `CoreSymbolKind.X`
- **Files modified:** `src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs`
- **Commit:** 28d6f5a (resolved before commit)

## Self-Check: PASSED

| Item | Status |
|------|--------|
| `src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs` | FOUND |
| `src/DocAgent.Ingestion/SymbolSorter.cs` | FOUND |
| `tests/DocAgent.Tests/RoslynSymbolGraphBuilderTests.cs` | FOUND |
| Commit 28d6f5a (Task 1) | FOUND |
| Commit 921d04f (Task 2) | FOUND |
| All 48 tests pass | VERIFIED |
