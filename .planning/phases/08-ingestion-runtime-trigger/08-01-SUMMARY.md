---
phase: 08-ingestion-runtime-trigger
plan: 01
subsystem: mcp-server-ingestion
tags: [ingestion, mcp-tool, pipeline-orchestration, concurrency, progress-notifications]
dependency_graph:
  requires: [DocAgent.Ingestion.LocalProjectSource, DocAgent.Ingestion.RoslynSymbolGraphBuilder, DocAgent.Ingestion.SnapshotStore, DocAgent.Indexing.BM25SearchIndex, DocAgent.McpServer.Security.PathAllowlist]
  provides: [IIngestionService, IngestionService, IngestionTools, ingest_project MCP tool]
  affects: [DocAgent.McpServer.ServiceCollectionExtensions, DocAgent.McpServer.Config.DocAgentServerOptions, DocAgent.Ingestion.LocalProjectSource]
tech_stack:
  added: []
  patterns: [SemaphoreSlim per-path concurrency, CancellationTokenSource linked timeout, PipelineOverride test seam, McpServerToolType DI injection]
key_files:
  created:
    - src/DocAgent.McpServer/Ingestion/IIngestionService.cs
    - src/DocAgent.McpServer/Ingestion/IngestionService.cs
    - src/DocAgent.McpServer/Ingestion/IngestionResult.cs
    - src/DocAgent.McpServer/Tools/IngestionTools.cs
    - tests/DocAgent.Tests/IngestionServiceTests.cs
    - tests/DocAgent.Tests/IngestionToolTests.cs
  modified:
    - src/DocAgent.McpServer/Config/DocAgentServerOptions.cs
    - src/DocAgent.McpServer/ServiceCollectionExtensions.cs
    - src/DocAgent.Ingestion/LocalProjectSource.cs
    - src/DocAgent.McpServer/DocAgent.McpServer.csproj
decisions:
  - "PipelineOverride internal test seam on IngestionService avoids real Roslyn/MSBuild in unit tests while keeping production path clean"
  - "requestContext?.Params?.Meta?[\"progressToken\"] for null-safe progress token extraction (Meta is JsonObject in MCP SDK 1.0.0)"
  - "IngestionService uses InternalsVisibleTo DocAgent.Tests for PipelineOverride seam access"
  - "Parallel test uses separate SnapshotStore artifact dirs to avoid manifest.json file contention"
metrics:
  duration_seconds: 887
  completed_date: "2026-02-28"
  tasks_completed: 2
  files_created: 6
  files_modified: 4
---

# Phase 08 Plan 01: Ingestion Runtime Trigger Summary

**One-liner:** `ingest_project` MCP tool with IngestionService pipeline orchestrator — per-path SemaphoreSlim concurrency, configurable timeout, glob filtering, progress notifications, and soft index-failure tolerance.

## What Was Built

### IngestionResult record
Immutable result returned from `IIngestionService.IngestAsync`: `SnapshotId`, `SymbolCount`, `ProjectCount`, `Duration`, `Warnings`, `IndexError?`.

### IIngestionService interface
Contract for the pipeline orchestrator. Single `IngestAsync` method with path, include/exclude globs, forceReindex, optional progress callback, and cancellation token.

### IngestionService (singleton)
Full pipeline orchestration: discover → parse → snapshot → index.
- `ConcurrentDictionary<string, SemaphoreSlim>` for per-path serialization — different projects run in parallel, same project is serialized.
- Linked `CancellationTokenSource` enforces `DocAgentServerOptions.IngestionTimeoutSeconds` (default 300).
- `ApplyGlobs` helper uses `Microsoft.Extensions.FileSystemGlobbing.Matcher` with root-stripping for absolute paths.
- Index failure is soft: snapshot is saved, `IndexError` is set in result, no exception thrown.
- `PipelineOverride` internal property enables test injection without real Roslyn/MSBuild.

### IngestionTools ([McpServerToolType])
`ingest_project` MCP tool:
- PathAllowlist check before any pipeline work (fail-fast `access_denied`).
- Progress notifications only sent when `progressToken` is non-null (MCP spec requirement).
- OTel tracing via `DocAgentTelemetry.Source.StartActivity`.
- Structured JSON response: `snapshotId`, `symbolCount`, `projectCount`, `durationMs`, `warnings`, `indexError`.
- `OperationCanceledException` propagates (handled by AuditFilter).
- Other exceptions logged, returned as `ingestion_failed` JSON.

### DocAgentServerOptions
Added `IngestionTimeoutSeconds` property (default 300).

### ServiceCollectionExtensions
Added `services.AddSingleton<IIngestionService, IngestionService>()`.

### LocalProjectSource
Added `.slnx` path support (routes to existing `DiscoverFromSolutionAsync`). Updated error message to include `.slnx`.

## Tests

| Test Class | Count | What is Tested |
|------------|-------|----------------|
| IngestionServiceTests | 8 | Correct counts, glob include, glob exclude, timeout, parallel different paths, serialized same path, index failure, progress callback |
| IngestionToolTests | 7 | Null path, empty path, access denied, valid path normalised, response shape, param forwarding, indexError in response |
| **Total new** | **15** | — |

Full suite: 172 tests, 0 failures (was 157 before this plan).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] requestContext null-conditional operator**
- **Found during:** Task 2 (IngestionToolTests)
- **Issue:** `requestContext.Params?.Meta?[...]` throws `NullReferenceException` in tests where `requestContext` is `null!`
- **Fix:** Changed to `requestContext?.Params?.Meta?[...]`
- **Files modified:** `src/DocAgent.McpServer/Tools/IngestionTools.cs`
- **Commit:** 464d2b9

**2. [Rule 2 - Missing functionality] PipelineOverride test seam**
- **Found during:** Task 1 (IngestionServiceTests design)
- **Issue:** `IngestionService` creates `LocalProjectSource` and `RoslynSymbolGraphBuilder` internally — no injection point for tests to avoid real Roslyn/MSBuild
- **Fix:** Added `internal Func<...>? PipelineOverride` property + `InternalsVisibleTo` attribute
- **Files modified:** `src/DocAgent.McpServer/Ingestion/IngestionService.cs`, `src/DocAgent.McpServer/DocAgent.McpServer.csproj`
- **Commit:** 6dc5c33

**3. [Rule 1 - Bug] Parallel test manifest file contention**
- **Found during:** Task 1 (IngestionServiceTests - IngestAsync_DifferentPaths_RunInParallel_NoDeadlock)
- **Issue:** Two `IngestionService` instances sharing a single `SnapshotStore` dir caused `IOException` on `manifest.json.tmp` during parallel saves
- **Fix:** Use separate artifact directories (`artifacts-a`, `artifacts-b`) per service instance in the parallel test
- **Files modified:** `tests/DocAgent.Tests/IngestionServiceTests.cs`
- **Commit:** 6dc5c33

**4. [Rule 2 - Missing functionality] SnapshotStore.SaveAsync named ct parameter**
- **Found during:** Task 1 (IngestionService.cs compilation)
- **Issue:** `SaveAsync(snapshot, ct)` resolved to `SaveAsync(snapshot, gitCommitSha?: ct)` since `ct` is the 3rd optional arg
- **Fix:** Changed to `SaveAsync(snapshot, ct: ct)` using named parameter
- **Files modified:** `src/DocAgent.McpServer/Ingestion/IngestionService.cs`
- **Commit:** 6dc5c33

## Self-Check: PASSED

All created files exist on disk. Both task commits verified in git log (6dc5c33, 464d2b9). Full test suite: 172 passed, 0 failed.
