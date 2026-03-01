---
phase: 10-incremental-ingestion
plan: 01
subsystem: ingestion
tags: [csharp, dotnet, messagepack, sha256, incremental-ingestion, domain-types]

requires:
  - phase: 09-semantic-diff-engine
    provides: SymbolGraphSnapshot domain record pattern (ContractlessStandardResolver, last-param optional field convention)

provides:
  - IngestionMetadata, FileChangeRecord, FileChangeKind domain types in DocAgent.Core
  - SymbolGraphSnapshot extended with optional IngestionMetadata? (last position, backward-compatible)
  - FileHashManifest record with SHA-256 hashing, ManifestDiff, atomic JSON persistence

affects:
  - 10-02 (IncrementalIngestionEngine builds against these contracts)
  - 10-03 (test suite exercises these types)

tech-stack:
  added: [System.Security.Cryptography.SHA256.HashDataAsync]
  patterns:
    - Optional record param at tail of SymbolGraphSnapshot for MessagePack ContractlessStandardResolver backward compat
    - Atomic JSON write via temp-file + File.Move(overwrite:true) matching SnapshotStore pattern
    - IDisposable test class with temp directory for file-system tests

key-files:
  created:
    - src/DocAgent.Core/IngestionMetadata.cs
    - src/DocAgent.Ingestion/FileHashManifest.cs
    - tests/DocAgent.Tests/IncrementalIngestion/IngestionMetadataTests.cs
    - tests/DocAgent.Tests/IncrementalIngestion/FileHashManifestTests.cs
  modified:
    - src/DocAgent.Core/Symbols.cs

key-decisions:
  - "IngestionMetadata? added as last positional param of SymbolGraphSnapshot for ContractlessStandardResolver backward compatibility"
  - "FileHasher is a public static class (stateless utility); ManifestDiff is a record with HasChanges and ChangedFiles computed properties"
  - "SHA-256 via SHA256.HashDataAsync (streaming, async); lowercase hex output for consistency"

patterns-established:
  - "File-system tests use IDisposable with a per-test GUID temp directory — cleaned up in Dispose()"
  - "Atomic JSON persistence: write to .tmp, then File.Move overwrite=true"

requirements-completed: [R-INCR-INGEST]

duration: 15min
completed: 2026-02-28
---

# Phase 10 Plan 01: Incremental Ingestion Domain Types Summary

**IngestionMetadata and FileHashManifest domain contracts established: SHA-256-based file change detection, MessagePack-safe snapshot extension, and atomic JSON persistence via temp-rename**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-02-28T00:00:00Z
- **Completed:** 2026-02-28T00:15:00Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments

- Added `FileChangeKind`, `FileChangeRecord`, and `IngestionMetadata` records to `DocAgent.Core` — the metadata contract that travels with each snapshot
- Extended `SymbolGraphSnapshot` with `IngestionMetadata? IngestionMetadata = null` as the last positional parameter (ContractlessStandardResolver-safe, backward compatible with existing serialized snapshots)
- Implemented `FileHashManifest`, `FileHasher`, and `ManifestDiff` in `DocAgent.Ingestion` with SHA-256 hashing, null-safe diffing, and atomic JSON save/load matching `SnapshotStore` patterns
- 11 unit tests: 3 for IngestionMetadata MessagePack round-trips (null, non-null, backward compat), 8 for FileHashManifest (hash correctness, diff scenarios, JSON round-trip)

## Task Commits

1. **Task 1: IngestionMetadata domain types + SymbolGraphSnapshot extension** - `aed19c5` (feat)
2. **Task 2: FileHashManifest with SHA-256, diff, JSON persistence** - `00aaf51` (feat)

## Files Created/Modified

- `src/DocAgent.Core/IngestionMetadata.cs` - FileChangeKind enum, FileChangeRecord, IngestionMetadata records
- `src/DocAgent.Core/Symbols.cs` - Added `IngestionMetadata? IngestionMetadata = null` as last param of SymbolGraphSnapshot
- `src/DocAgent.Ingestion/FileHashManifest.cs` - FileHashManifest record, FileHasher static class, ManifestDiff record
- `tests/DocAgent.Tests/IncrementalIngestion/IngestionMetadataTests.cs` - MessagePack round-trip tests
- `tests/DocAgent.Tests/IncrementalIngestion/FileHashManifestTests.cs` - SHA-256, diff, and JSON persistence tests

## Decisions Made

- IngestionMetadata? placed last in SymbolGraphSnapshot to maintain ContractlessStandardResolver backward compatibility (matches pattern from Phase 09 SymbolNode extensions)
- FileHasher is a static class (stateless), ManifestDiff is a record with `HasChanges` and `ChangedFiles` computed convenience members
- SHA-256 via `SHA256.HashDataAsync(stream, ct)` for streaming async hashing; returns lowercase 64-char hex

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Type contracts are in place; Plan 02 (IncrementalIngestionEngine) can build directly against `IngestionMetadata`, `FileHashManifest`, and `FileHasher`
- All existing tests continue to pass (SymbolGraphSnapshot change is backward-compatible due to default parameter)

---
*Phase: 10-incremental-ingestion*
*Completed: 2026-02-28*
