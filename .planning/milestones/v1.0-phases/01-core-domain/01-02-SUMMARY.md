---
phase: 01-core-domain
plan: 02
subsystem: core-domain
tags: [serialization, messagepack, contenthash, xxhash64, determinism, tests]
dependency_graph:
  requires: [01-01]
  provides: [snapshot-serialization-contract, SerializationFormat-enum, deterministic-content-hash]
  affects: [phase-02-ingestion, snapshot-store, phase-03-indexing]
tech_stack:
  added: [MessagePack 3.1.4, System.IO.Hashing 9.0.0]
  patterns: [ContractlessStandardResolver, XxHash64-content-hash, JSON-debug-roundtrip]
key_files:
  created:
    - path: tests/DocAgent.Tests/SnapshotSerializationTests.cs
      description: 5 CORE-02 tests for MessagePack roundtrip, determinism, content hash stability, JSON roundtrip, and hash divergence
  modified:
    - path: src/DocAgent.Core/Symbols.cs
      description: Added SerializationFormat enum (MessagePack, Json, Tron)
    - path: src/DocAgent.Core/DocAgent.Core.csproj
      description: Added MessagePack PackageReference
    - path: Directory.Packages.props
      description: Added MessagePack 3.1.4 and System.IO.Hashing 9.0.0 package versions
    - path: src/Directory.Packages.props
      description: Fixed to enable ManagePackageVersionsCentrally and import root props file
    - path: tests/DocAgent.Tests/DocAgent.Tests.csproj
      description: Added MessagePack and System.IO.Hashing PackageReferences
    - path: src/DocAgent.Indexing/InMemorySearchIndex.cs
      description: Fixed pre-existing CS1998 async warning suppression for SearchAsync
decisions:
  - ContractlessStandardResolver used for MessagePack so domain types need no serialization attributes
  - System.IO.Hashing 9.0.0 required as explicit package (not auto-included in .NET 10)
  - JSON roundtrip test uses IncludeFields=true to handle value tuple (string, string) deserialization
  - Tron defined in SerializationFormat enum for contract completeness; implementations will throw NotSupportedException
metrics:
  duration_seconds: 364
  completed_date: 2026-02-26
  tasks_completed: 2
  tasks_total: 2
  files_created: 1
  files_modified: 6
---

# Phase 1 Plan 02: Snapshot Serialization Contract Summary

**One-liner:** Added MessagePack 3.1.4 serialization with ContractlessStandardResolver to SymbolGraphSnapshot and 5 CORE-02 tests proving deterministic roundtrip and stable XxHash64 content hash.

## What Was Built

### Task 1: Add MessagePack package and SerializationFormat enum

- **`SerializationFormat` enum** added to `src/DocAgent.Core/Symbols.cs` with three members: `MessagePack`, `Json`, `Tron`
- **MessagePack 3.1.4** added to root `Directory.Packages.props` and referenced in `DocAgent.Core.csproj`
- **`src/Directory.Packages.props`** fixed to enable `ManagePackageVersionsCentrally=true` and import the root props file (was empty, causing CPM to not propagate to src/ projects)
- No `[MessagePackObject]` or `[Key]` attributes on domain types — uses `ContractlessStandardResolver`

### Task 2: Create SnapshotSerializationTests

Five tests in `tests/DocAgent.Tests/SnapshotSerializationTests.cs`:

1. `Roundtrip_MessagePack_produces_identical_snapshot` — serialize to byte[], deserialize, assert all fields match via `.BeEquivalentTo()`
2. `Serialization_is_deterministic` — two separate serializations produce identical byte arrays
3. `ContentHash_is_stable_across_serializations` — XxHash64 hash string is identical for two serializations of the same snapshot
4. `Json_roundtrip_produces_equivalent_snapshot` — System.Text.Json round-trip with `IncludeFields=true` for tuple fields
5. `ContentHash_differs_for_different_snapshots` — different snapshots produce different content hash values

All 15 tests pass (5 new + 7 pre-existing + 3 InMemorySearchIndexTests).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed src/Directory.Packages.props to enable CPM**
- **Found during:** Task 1 (dotnet restore)
- **Issue:** `src/Directory.Packages.props` was empty (from 01-01 clearing), causing MSBuild to stop traversal there without enabling `ManagePackageVersionsCentrally`. Projects inside `src/` could not resolve package versions from the root file.
- **Fix:** Added `ManagePackageVersionsCentrally=true` and an `<Import>` of the root `Directory.Packages.props` to `src/Directory.Packages.props`.
- **Files modified:** src/Directory.Packages.props
- **Commit:** 4485703

**2. [Rule 2 - Missing dependency] Added System.IO.Hashing 9.0.0**
- **Found during:** Task 2 (compilation)
- **Issue:** Plan stated XxHash64 is "in-box for .NET 10" but it still requires an explicit `System.IO.Hashing` NuGet package reference. CS0234 error.
- **Fix:** Added `System.IO.Hashing 9.0.0` to `Directory.Packages.props` and to test project references.
- **Files modified:** Directory.Packages.props, tests/DocAgent.Tests/DocAgent.Tests.csproj
- **Commit:** d944366

**3. [Rule 1 - Bug] Fixed JSON roundtrip for value tuple deserialization**
- **Found during:** Task 2 (test execution)
- **Issue:** `IReadOnlyList<(string Type, string Description)>` deserializes tuple `Item1`/`Item2` as null with default `JsonSerializerOptions`. Value tuples require `IncludeFields=true` for System.Text.Json to deserialize the backing fields.
- **Fix:** Added `IncludeFields = true` to `JsonSerializerOptions` in the JSON roundtrip test.
- **Files modified:** tests/DocAgent.Tests/SnapshotSerializationTests.cs
- **Commit:** d944366

**4. [Rule 1 - Bug] Fixed pre-existing CS1998 in InMemorySearchIndex.SearchAsync**
- **Found during:** Task 2 (test run against full suite)
- **Issue:** `async IAsyncEnumerable<SearchHit> SearchAsync` lacked any `await`, causing CS1998 warning treated as error.
- **Fix:** Added `#pragma warning disable/restore CS1998` around the method.
- **Files modified:** src/DocAgent.Indexing/InMemorySearchIndex.cs
- **Commit:** d944366

## Verification Results

```
dotnet build src/DocAgentFramework.sln
  Build succeeded. 0 Errors. 0 Warnings.

dotnet test tests/DocAgent.Tests --filter "FullyQualifiedName~SnapshotSerializationTests"
  Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5

dotnet test tests/DocAgent.Tests
  Passed! - Failed: 0, Passed: 15, Skipped: 0, Total: 15
```

## Self-Check: PASSED
