---
phase: 14-solution-ingestion-pipeline
verified: 2026-03-01T00:00:00Z
status: passed
score: 8/8 must-haves verified
re_verification: false
---

# Phase 14: Solution Ingestion Pipeline Verification Report

**Phase Goal:** An agent can ingest an entire .sln in one call; non-C# projects are skipped gracefully; MSBuild failures are detected and reported; the resulting snapshot carries per-node project attribution and is PathAllowlist-secured
**Verified:** 2026-03-01
**Status:** passed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                                    | Status     | Evidence                                                                                                                                       |
|----|----------------------------------------------------------------------------------------------------------|------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | SolutionIngestionService opens a .sln via MSBuildWorkspace.OpenSolutionAsync and produces a merged SymbolGraphSnapshot | VERIFIED   | `SolutionIngestionService.cs` lines 65–88: `MSBuildWorkspace.Create()` + `OpenSolutionAsync(slnPath)`, snapshot built and persisted via `_store.SaveAsync` |
| 2  | Non-C# projects are skipped with status "skipped" and reason containing the language name               | VERIFIED   | Lines 154–164: `project.Language != LanguageNames.CSharp` guard with `$"Unsupported language: {project.Language}"` reason                     |
| 3  | Multi-targeted projects are deduplicated — only the highest TFM entry is processed                      | VERIFIED   | Lines 97–123: groups by `FilePath`, picks `OrderByDescending(p => ExtractTfmVersion(p.Name)).First()`. `ExtractTfmVersion` verified correct by 2 dedicated unit tests |
| 4  | MSBuild failures (null compilation) produce status "failed"; other projects still succeed               | VERIFIED   | Lines 185–195: null-compilation check returns `"failed"` + `"Could not obtain compilation"`. Compile exceptions also caught (lines 168–183). Loop continues for other projects |
| 5  | Every SymbolNode in the output has ProjectOrigin set to the originating project name                    | VERIFIED   | Lines 203–208: `projectNodes.Select(n => n with { ProjectOrigin = projectName }).ToList()` stamps all nodes before merging into `allNodes`     |
| 6  | ingest_solution MCP tool with valid .sln returns structured JSON with snapshotId, solutionName, projects, warnings | VERIFIED   | `IngestionTools.cs` lines 144–238: `[McpServerTool(Name = "ingest_solution")]` method serializes all 9 fields; `SolutionIngestionToolTests` test 4 asserts every field |
| 7  | Calling ingest_solution with path outside PathAllowlist returns opaque access_denied error              | VERIFIED   | Lines 161–167: `_allowlist.IsAllowed(absolutePath)` gate returns `ErrorJson("access_denied", "Path is not in the configured allow list.")`; `SolutionIngestionToolTests` test 1 asserts no service call |
| 8  | ISolutionIngestionService is registered in DI and injected into IngestionTools                          | VERIFIED   | `ServiceCollectionExtensions.cs` line 36: `services.AddSingleton<ISolutionIngestionService, SolutionIngestionService>()`; `IngestionTools` constructor injects it (line 35) |

**Score:** 8/8 truths verified

---

## Required Artifacts

| Artifact                                                                | Expected                                      | Status    | Details                                    |
|-------------------------------------------------------------------------|-----------------------------------------------|-----------|--------------------------------------------|
| `src/DocAgent.McpServer/Ingestion/ISolutionIngestionService.cs`         | Interface contract for solution ingestion     | VERIFIED  | Exports `ISolutionIngestionService` with `IngestAsync` method signature |
| `src/DocAgent.McpServer/Ingestion/SolutionIngestionService.cs`          | Full implementation (min 100 lines)           | VERIFIED  | 467 lines; language filter, TFM dedup, MSBuild failures, ProjectOrigin stamping, SnapshotStore persistence |
| `src/DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs`           | Result records                                | VERIFIED  | Exports `SolutionIngestionResult` and `ProjectIngestionStatus` with all required fields |
| `tests/DocAgent.Tests/SolutionIngestionServiceTests.cs`                 | Unit tests via PipelineOverride seam (min 80 lines) | VERIFIED  | 265 lines; 7 tests covering happy path, partial success, all-failed, persistence, TFM extraction, progress callback |
| `src/DocAgent.McpServer/Tools/IngestionTools.cs`                        | IngestSolution MCP tool method                | VERIFIED  | Contains `[McpServerTool(Name = "ingest_solution")]`, `_solutionIngestionService` field |
| `src/DocAgent.McpServer/ServiceCollectionExtensions.cs`                 | DI registration for ISolutionIngestionService | VERIFIED  | `services.AddSingleton<ISolutionIngestionService, SolutionIngestionService>()` at line 36 |
| `tests/DocAgent.Tests/SolutionIngestionToolTests.cs`                    | Tool-level tests (min 40 lines)               | VERIFIED  | 210 lines; 5 tests covering allowlist denial, allowed path, empty path, response shape, exception handling |

---

## Key Link Verification

| From                          | To                              | Via                                           | Status  | Details                                                                              |
|-------------------------------|---------------------------------|-----------------------------------------------|---------|--------------------------------------------------------------------------------------|
| `SolutionIngestionService`    | `RoslynSymbolGraphBuilder`      | `GetCompilationAsync` + `WalkCompilationAsync` | WIRED   | Line 128: `new RoslynSymbolGraphBuilder(parser, resolver, ...)`. Line 200: `WalkCompilationAsync(builder, compilation, ...)` |
| `SolutionIngestionService`    | `SnapshotStore`                 | `_store.SaveAsync`                            | WIRED   | Line 236: `await _store.SaveAsync(snapshot, ct: cancellationToken)` — result used to build return value |
| `IngestionTools.IngestSolution` | `ISolutionIngestionService.IngestAsync` | DI-injected `_solutionIngestionService` field | WIRED   | Line 199: `await _solutionIngestionService.IngestAsync(absolutePath, progressCallback, cancellationToken)` |
| `IngestionTools.IngestSolution` | `PathAllowlist.IsAllowed`       | Security gate before delegation               | WIRED   | Lines 161–167: `_allowlist.IsAllowed(absolutePath)` checked before service call; non-allowed path returns early with error |

---

## Requirements Coverage

| Requirement | Source Plan | Description                                                                           | Status    | Evidence                                                                                      |
|-------------|-------------|---------------------------------------------------------------------------------------|-----------|-----------------------------------------------------------------------------------------------|
| INGEST-01   | 14-01, 14-02 | Agent can ingest entire .sln in one call via `ingest_solution` MCP tool               | SATISFIED | `ingest_solution` tool exists with `[McpServerTool]` attribute; delegates to `SolutionIngestionService.IngestAsync` |
| INGEST-02   | 14-01       | Non-C# projects skipped gracefully with logged warnings                               | SATISFIED | Language filter produces `"skipped"` status with language in reason; warnings list captures `WorkspaceFailed` events |
| INGEST-03   | 14-01       | Multi-targeting projects deduplicated to single TFM                                   | SATISFIED | Group-by-FilePath dedup with `ExtractTfmVersion` ordering; `ChosenTfm` recorded in status |
| INGEST-04   | 14-01       | MSBuildWorkspace load failures detected and reported                                  | SATISFIED | `workspace.WorkspaceFailed` handler adds to warnings list; null/exception compilation produces `"failed"` status |
| INGEST-06   | 14-01, 14-02 | `ingest_solution` secured with PathAllowlist enforcement                              | SATISFIED | `_allowlist.IsAllowed` gate in `IngestSolution` mirrors `ingest_project` pattern exactly; opaque `"access_denied"` error |

**Note:** INGEST-05 (per-project incremental re-ingestion) is mapped to Phase 17 and is not claimed by Phase 14 plans. No orphaned requirements detected.

---

## Anti-Patterns Found

No blockers or stubs detected. Scan of all five phase files:

- No `TODO`, `FIXME`, `XXX`, `HACK`, or `PLACEHOLDER` comments
- No `return null` / empty stubs in implementation paths
- `WalkCompilationAsync` delegates to `WalkNamespaceInline` which performs real namespace traversal (not a stub)
- `PipelineOverride` seam is clearly internal/test-only and not exposed in the interface

---

## Human Verification Required

### 1. Real MSBuildWorkspace Integration

**Test:** Point `ingest_solution` at an actual .sln file (e.g., `src/DocAgentFramework.sln`) via the running MCP server.
**Expected:** Returns JSON with populated `projects` array, non-zero `totalNodeCount`, and `snapshotId`.
**Why human:** Unit tests use `PipelineOverride` to bypass MSBuild entirely. Real workspace loading depends on SDK/MSBuild availability at runtime, which cannot be verified by code inspection.

### 2. Non-C# Project Skip in Real Solution

**Test:** Add an F# project to a test solution and ingest it.
**Expected:** F# project appears in `projects` array with `status: "skipped"` and `reason` containing "F#".
**Why human:** Language filtering logic is correct in code but requires real MSBuild workspace to exercise the `project.Language` property on a live Roslyn `Project` object.

### 3. PathAllowlist Configuration in Production

**Test:** Configure `AllowedPaths` in `appsettings.json`, start the MCP server, and attempt `ingest_solution` with both an allowed and a denied path.
**Expected:** Allowed path proceeds to ingestion; denied path returns `{"error":"access_denied","message":"Path is not in the configured allow list."}`.
**Why human:** Unit tests construct `PathAllowlist` directly with hardcoded options. Real server configuration wiring is not verified programmatically.

---

## Gaps Summary

No gaps. All automated checks passed.

Build: clean (0 warnings, 0 errors, TreatWarningsAsErrors=true).
Tests: 12 phase-specific tests pass (7 service-level + 5 tool-level). Full suite passes.

---

_Verified: 2026-03-01_
_Verifier: Claude (gsd-verifier)_
