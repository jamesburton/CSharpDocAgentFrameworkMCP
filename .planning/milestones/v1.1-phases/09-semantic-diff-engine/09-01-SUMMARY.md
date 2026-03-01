---
phase: 09-semantic-diff-engine
plan: 01
subsystem: core-types
tags: [diff-types, symbol-node, roslyn, domain-model]
dependency_graph:
  requires: []
  provides: [DiffTypes.cs, ParameterInfo, GenericConstraint, SymbolNode-signature-fields]
  affects: [09-02-PLAN.md, 09-03-PLAN.md]
tech_stack:
  added: []
  patterns: [sealed-records, flat-list-diff, MessagePack-contractless]
key_files:
  created:
    - src/DocAgent.Core/DiffTypes.cs
  modified:
    - src/DocAgent.Core/Symbols.cs
    - src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs
    - tests/DocAgent.Tests/BM25SearchIndexPersistenceTests.cs
    - tests/DocAgent.Tests/BM25SearchIndexTests.cs
    - tests/DocAgent.Tests/E2EIntegrationTests.cs
    - tests/DocAgent.Tests/GetReferencesAsyncTests.cs
    - tests/DocAgent.Tests/IngestionServiceTests.cs
    - tests/DocAgent.Tests/InMemorySearchIndexTests.cs
    - tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs
    - tests/DocAgent.Tests/McpIntegrationTests.cs
    - tests/DocAgent.Tests/McpToolTests.cs
    - tests/DocAgent.Tests/SnapshotSerializationTests.cs
    - tests/DocAgent.Tests/SnapshotStoreTests.cs
    - tests/DocAgent.Tests/SymbolIdTests.cs
decisions:
  - "SymbolNode extended with three optional fields at end of record for backward-compatible addition"
  - "RoslynSymbolGraphBuilder refactored to ExtractSignatureFields dispatch method per Roslyn symbol type"
  - "DiffTypes.cs uses per-category nullable detail fields on SymbolChange for MessagePack ContractlessStandardResolver compatibility"
metrics:
  duration: "~20 minutes"
  completed: "2026-02-28"
  tasks_completed: 3
  files_changed: 14
---

# Phase 9 Plan 01: Diff Domain Types and SymbolNode Extension Summary

**One-liner:** Structured ParameterInfo/GenericConstraint records added to SymbolNode, complete DiffTypes.cs hierarchy with seven sealed record types and three enums, Roslyn builder populated from IMethodSymbol/IPropertySymbol/IFieldSymbol/INamedTypeSymbol.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Extend SymbolNode with structured signature fields | 0527d08 | Symbols.cs + 9 test files |
| 2 | Create DiffTypes.cs with complete diff domain types | ebe2c34 | DiffTypes.cs (new) |
| 3 | Populate SymbolNode fields in RoslynSymbolGraphBuilder | 16f566a | RoslynSymbolGraphBuilder.cs + 4 test files |

## What Was Built

### SymbolNode Extension (Symbols.cs)

Added two new supporting records and three new fields to SymbolNode:

- `ParameterInfo` — structured parameter data (Name, TypeName, DefaultValue, IsParams/Ref/Out/In)
- `GenericConstraint` — type parameter constraint (TypeParameterName, Constraints list)
- `SymbolNode.ReturnType` — nullable string, null for void/constructors/non-method symbols
- `SymbolNode.Parameters` — IReadOnlyList<ParameterInfo>, empty for non-callable symbols
- `SymbolNode.GenericConstraints` — IReadOnlyList<GenericConstraint>, populated for methods and types

### DiffTypes.cs (new file)

Complete diff domain hierarchy:

**Enums:** `ChangeType` (Added/Removed/Modified), `ChangeCategory` (six categories), `ChangeSeverity` (Breaking/NonBreaking/Informational)

**Change detail records (6):** `SignatureChangeDetail`, `NullabilityChangeDetail`, `ConstraintChangeDetail`, `AccessibilityChangeDetail`, `DependencyChangeDetail`, `DocCommentChangeDetail`

**`ParameterChange`** — parameter-level change within a signature change

**`SymbolChange`** — single change entry with per-category nullable detail fields (MessagePack safe, no polymorphic base)

**`DiffSummary`** — aggregate statistics (TotalChanges, Added, Removed, Modified, Breaking, NonBreaking, Informational)

**`SymbolDiff`** — top-level flat list result (BeforeSnapshotVersion, AfterSnapshotVersion, ProjectName, Summary, Changes)

### RoslynSymbolGraphBuilder

Added `ExtractSignatureFields` private static method that dispatches on Roslyn symbol type:
- `IMethodSymbol` → ReturnType (skip void/constructors), Parameters from IParameterSymbol, GenericConstraints from TypeParameters
- `IPropertySymbol` → ReturnType from property.Type, Parameters for indexers only
- `IFieldSymbol` → ReturnType from field.Type, empty Parameters/Constraints
- `INamedTypeSymbol` → GenericConstraints from type.TypeParameters, null ReturnType
- All others → null/empty

Added `ExtractTypeParameterConstraints` helper handling class/struct/notnull/unmanaged/new() and ConstraintTypes.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Missing call site updates in 4 additional test files**
- **Found during:** Task 3 test run
- **Issue:** grep for `new SymbolNode(` missed files using helper methods (BM25SearchIndexTests, GetReferencesAsyncTests, E2EIntegrationTests, McpToolTests) — these use factory helpers that construct SymbolNode internally
- **Fix:** Updated all 4 helper methods with the three new fields
- **Files modified:** BM25SearchIndexTests.cs, GetReferencesAsyncTests.cs, E2EIntegrationTests.cs, McpToolTests.cs
- **Commit:** 16f566a

## Verification

- `dotnet build src/DocAgent.Core/DocAgent.Core.csproj` — 0 errors, 0 warnings
- `dotnet build src/DocAgent.Ingestion/DocAgent.Ingestion.csproj` — 0 errors, 0 warnings
- `dotnet build tests/DocAgent.Tests/DocAgent.Tests.csproj` — 0 errors, 0 warnings
- `dotnet test --filter RoslynSymbolGraphBuilder` — 8/8 passed

## Self-Check: PASSED

Files exist:
- src/DocAgent.Core/DiffTypes.cs — FOUND
- src/DocAgent.Core/Symbols.cs (contains ParameterInfo) — FOUND

Commits exist:
- 0527d08 — FOUND
- ebe2c34 — FOUND
- 16f566a — FOUND
