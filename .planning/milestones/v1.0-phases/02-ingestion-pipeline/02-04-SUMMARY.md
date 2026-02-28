---
phase: 02-ingestion-pipeline
plan: "04"
subsystem: ingestion
tags: [snapshot, persistence, messagepack, hashing, manifest]
dependency_graph:
  requires: [02-01]
  provides: [SnapshotStore, SnapshotManifestEntry]
  affects: [DocAgent.Ingestion, DocAgent.Tests]
tech_stack:
  added: [MessagePack (Ingestion project), System.IO.Hashing (Ingestion project)]
  patterns: [ContractlessStandardResolver, XxHash128, atomic-file-write, content-addressed-storage]
key_files:
  created:
    - src/DocAgent.Ingestion/SnapshotStore.cs
    - tests/DocAgent.Tests/SnapshotStoreTests.cs
  modified:
    - src/DocAgent.Ingestion/DocAgent.Ingestion.csproj
decisions:
  - "ContentHash computed over bytes with ContentHash=null to avoid circular dependency; final file contains snapshot with hash set"
  - "Atomic manifest update: write to .tmp file then rename with overwrite:true"
  - "Duplicate SaveAsync call (same content) replaces existing manifest entry rather than appending"
metrics:
  duration: "12 min"
  completed: "2026-02-26"
  tasks_completed: 2
  files_changed: 3
---

# Phase 2 Plan 4: SnapshotStore Persistence Summary

**One-liner:** Content-addressed MessagePack snapshot store with XxHash128 hashing and atomic manifest.json index.

## What Was Built

`SnapshotStore` in `DocAgent.Ingestion` provides read/write access to versioned `SymbolGraphSnapshot` artifacts on the filesystem. Snapshots are serialized via MessagePack (`ContractlessStandardResolver`), content-hashed via XxHash128 (128-bit, hex string), and stored as `artifacts/{hash}.msgpack`. A `manifest.json` tracks all stored snapshots with metadata for listing without file scanning.

### Key Implementation Details

- **SaveAsync:** Serializes with `ContentHash=null` → XxHash128 → set hash on snapshot → re-serialize → write file → update manifest
- **LoadAsync:** Reads `{hash}.msgpack`, deserializes; returns null if file not found
- **ListAsync:** Reads `manifest.json`, returns typed `SnapshotManifestEntry` list
- **Manifest update:** Atomic write via temp file + `File.Move(overwrite:true)`; duplicate hashes replace existing entries

### Tests (8/8 passing)

| Test | Validates |
|------|-----------|
| `SaveAsync_writes_msgpack_file` | File exists at correct path |
| `SaveAsync_sets_content_hash` | Non-null hash returned |
| `SaveAsync_updates_manifest` | manifest.json entry with all metadata |
| `LoadAsync_roundtrips_snapshot` | All fields survive serialize/deserialize |
| `LoadAsync_nonexistent_returns_null` | Null for missing hash |
| `ListAsync_returns_all_entries` | Counts 2 entries after 2 saves |
| `SaveAsync_multiple_snapshots_coexist` | Two files + two manifest entries |
| `SaveAsync_deterministic_hash` | Same input = same hash = one file |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Nullable return type ambiguity in ListAsync**
- **Found during:** Task 1 build
- **Issue:** `manifest?.Snapshots ?? Array.Empty<SnapshotManifestEntry>()` — compiler error: `??` cannot be applied to `List<T>` and `T[]`
- **Fix:** Explicit cast `(IReadOnlyList<SnapshotManifestEntry>)Array.Empty<...>()`
- **Files modified:** `src/DocAgent.Ingestion/SnapshotStore.cs`
- **Commit:** 0c3884c (fixed before commit)

## Self-Check: PASSED

- [x] `src/DocAgent.Ingestion/SnapshotStore.cs` exists
- [x] `tests/DocAgent.Tests/SnapshotStoreTests.cs` exists
- [x] Commit 0c3884c (feat) — SnapshotStore implementation
- [x] Commit 0299f1b (test) — 8 SnapshotStore tests
- [x] `dotnet build src/DocAgentFramework.sln` — 0 errors
- [x] `dotnet test --filter SnapshotStoreTests` — 8/8 passed
