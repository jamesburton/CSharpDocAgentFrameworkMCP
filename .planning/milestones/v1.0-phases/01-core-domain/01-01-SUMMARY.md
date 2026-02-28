---
phase: 01-core-domain
plan: 01
subsystem: core-domain
tags: [domain-types, value-objects, enums, golden-file-tests, package-management]
dependency_graph:
  requires: []
  provides: [stable-domain-contracts, SymbolId-value-equality, PreviousIds-rename-tracking]
  affects: [all-downstream-plans, phase-02-ingestion, phase-03-indexing]
tech_stack:
  added: [Verify.Xunit 31.12.5, xunit 2.9.3, Microsoft.NET.Test.Sdk 18.0.1]
  patterns: [golden-file-testing, central-package-management, record-value-types]
key_files:
  created:
    - path: tests/DocAgent.Tests/SymbolIdTests.cs
      description: 5 CORE-01 tests for SymbolId value equality and PreviousIds rename tracking
    - path: tests/DocAgent.Tests/SymbolIdTests.PreviousIds_tracks_rename.verified.txt
      description: Verified golden-file snapshot for PreviousIds rename tracking
    - path: Directory.Packages.props
      description: Root-level central package management (ManagePackageVersionsCentrally=true)
    - path: Directory.Build.props
      description: Root-level build properties covering test project (ImplicitUsings, TreatWarningsAsErrors)
  modified:
    - path: src/DocAgent.Core/Symbols.cs
      description: Expanded all domain types to Phase 1 final shape
    - path: tests/DocAgent.Tests/InMemorySearchIndexTests.cs
      description: Updated SymbolNode and SymbolGraphSnapshot constructor calls for new parameters
    - path: src/Directory.Packages.props
      description: Cleared (version management consolidated at repository root)
    - path: tests/DocAgent.Tests/DocAgent.Tests.csproj
      description: Added Verify.Xunit, Microsoft.NET.Test.Sdk, CopyLocalLockFileAssemblies
decisions:
  - PreviousIds stored as IReadOnlyList on SymbolNode (not a dedicated edge) for V1 simplicity
  - Verify.Xunit 31.12.5 requires xunit 2.9.3 (upgraded from 2.7.1 for compatibility)
  - Root-level Directory.Packages.props created because tests/ is outside src/ tree; src/ file cleared
  - Root-level Directory.Build.props created so test project gets ImplicitUsings and TreatWarningsAsErrors
  - CopyLocalLockFileAssemblies=true added to test project to ensure transitive NuGet DLLs are copied to output
  - Microsoft.NET.Test.Sdk 18.0.1 added to align testhost with .NET 10 SDK expectations
  - ContentHash on SymbolGraphSnapshot is nullable (set by persistence layer to avoid circular dependency)
metrics:
  duration_seconds: 974
  completed_date: 2026-02-26
  tasks_completed: 2
  tasks_total: 2
  files_created: 4
  files_modified: 4
---

# Phase 1 Plan 01: Core Domain Type Expansion Summary

**One-liner:** Expanded all Symbols.cs domain types to Phase 1 final shape and added CORE-01 golden-file tests for SymbolId value equality and PreviousIds rename tracking using Verify.Xunit 31.12.5.

## What Was Built

### Task 1: Expand domain types in Symbols.cs

All domain types in `src/DocAgent.Core/Symbols.cs` expanded in-place:

- **SymbolKind** — grew from 7 to 14 members by adding: Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter
- **Accessibility** — new enum with 6 members: Public, Internal, Protected, Private, ProtectedInternal, PrivateProtected
- **DocComment** — expanded with TypeParams (`IReadOnlyDictionary<string, string>`), Exceptions (`IReadOnlyList<(string Type, string Description)>`), SeeAlso (`IReadOnlyList<string>`)
- **SymbolNode** — added PreviousIds (`IReadOnlyList<SymbolId>`) and Accessibility parameters
- **SymbolEdgeKind** — added Overrides and Returns after existing members
- **SymbolGraphSnapshot** — added ProjectName (`string`) and ContentHash (`string?`)

Updated `InMemorySearchIndexTests.cs` to pass new required parameters (PreviousIds: [], Accessibility: Accessibility.Public, ProjectName: "test", ContentHash: null).

### Task 2: Add Verify.Xunit and create SymbolIdTests

Five tests in `tests/DocAgent.Tests/SymbolIdTests.cs`:

1. `SymbolId_value_equality_holds` — two SymbolId instances with same string are equal
2. `SymbolId_inequality_for_different_values` — different strings are not equal
3. `SymbolId_works_as_dictionary_key` — dictionary lookup by equal-but-different instance
4. `PreviousIds_tracks_rename` — golden-file test verifying rename history in SymbolNode
5. `PreviousIds_empty_for_no_renames` — empty PreviousIds for symbol with no renames

Golden file verified and committed at `tests/DocAgent.Tests/SymbolIdTests.PreviousIds_tracks_rename.verified.txt`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created root-level Directory.Packages.props and Directory.Build.props**
- **Found during:** Task 2 (restore of test project)
- **Issue:** `tests/DocAgent.Tests/` is outside the `src/` directory tree, so `src/Directory.Packages.props` was unreachable by NuGet's upward traversal. Build had previously worked only with `--no-restore` using stale obj/bin artifacts.
- **Fix:** Created `Directory.Packages.props` at repo root with ManagePackageVersionsCentrally=true and all consolidated package versions. Cleared `src/Directory.Packages.props`. Created `Directory.Build.props` at root so test project inherits ImplicitUsings and TreatWarningsAsErrors.
- **Files modified:** Directory.Packages.props (new), Directory.Build.props (new), src/Directory.Packages.props (cleared)
- **Commits:** b323757

**2. [Rule 1 - Bug] Upgraded xunit 2.7.1 to 2.9.3 for Verify.Xunit compatibility**
- **Found during:** Task 2 (test restore)
- **Issue:** Verify.Xunit 31.x requires xunit.extensibility.execution 2.9.x; xunit 2.7.1 caused NU1107 version conflict.
- **Fix:** Upgraded xunit to 2.9.3, xunit.runner.visualstudio to 2.8.2, added Microsoft.NET.Test.Sdk 18.0.1.
- **Files modified:** Directory.Packages.props, tests/DocAgent.Tests/DocAgent.Tests.csproj
- **Commits:** b323757

**3. [Rule 3 - Blocking] Added CopyLocalLockFileAssemblies=true to test project**
- **Found during:** Task 2 (test execution)
- **Issue:** Transitive NuGet DLLs (Argon, DiffEngine, EmptyFiles, etc.) were not being copied to the output directory, causing testhost to abort at runtime.
- **Fix:** Added `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to test project.
- **Files modified:** tests/DocAgent.Tests/DocAgent.Tests.csproj
- **Commits:** b323757

**4. [Rule 1 - Bug] Changed [UsesVerify] to no attribute (Verify.Xunit 31 API change)**
- **Found during:** Task 2 (compilation)
- **Issue:** Plan specified `[UsesVerify]` attribute on the test class, but Verify.Xunit 31.x changed this to a build-generated assembly attribute `[UseVerify]` that is not applied at class level.
- **Fix:** Removed class-level attribute. In Verify.Xunit 31, the assembly attribute is auto-generated by the build target; no per-class attribute is needed.
- **Files modified:** tests/DocAgent.Tests/SymbolIdTests.cs
- **Commits:** b323757

## Verification Results

```
dotnet build src/DocAgentFramework.sln
  Build succeeded. 0 Errors. 0 C# Warnings.

dotnet test tests/DocAgent.Tests --filter "FullyQualifiedName~SymbolIdTests"
  Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5

dotnet test tests/DocAgent.Tests
  Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7
```

## Self-Check: PASSED

All created files verified on disk. All commits verified in git history.
- src/DocAgent.Core/Symbols.cs: FOUND
- tests/DocAgent.Tests/SymbolIdTests.cs: FOUND
- tests/DocAgent.Tests/SymbolIdTests.PreviousIds_tracks_rename.verified.txt: FOUND
- Directory.Packages.props: FOUND
- Directory.Build.props: FOUND
- Commit f7d8474: FOUND
- Commit b323757: FOUND
