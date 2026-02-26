---
phase: 02-ingestion-pipeline
plan: 01
subsystem: ingestion
tags: [roslyn, msbuild, project-discovery, local-filesystem]
dependency_graph:
  requires: [DocAgent.Core/IProjectSource, DocAgent.Core/ProjectLocator, DocAgent.Core/ProjectInventory]
  provides: [DocAgent.Ingestion/LocalProjectSource]
  affects: [DocAgent.Tests/LocalProjectSourceTests]
tech_stack:
  added: [Microsoft.CodeAnalysis.Workspaces.MSBuild 4.12.0, Microsoft.CodeAnalysis.CSharp.Workspaces 4.12.0]
  patterns: [MSBuildWorkspace, NuGetAuditMode=direct for transitive vulnerability suppression]
key_files:
  created:
    - src/DocAgent.Ingestion/LocalProjectSource.cs
    - tests/DocAgent.Tests/LocalProjectSourceTests.cs
  modified:
    - Directory.Packages.props
    - src/DocAgent.Ingestion/DocAgent.Ingestion.csproj
    - tests/DocAgent.Tests/DocAgent.Tests.csproj
decisions:
  - "Action<string> logWarning used instead of ILogger to keep Ingestion free of Microsoft.Extensions.Logging dependency"
  - "NuGetAuditMode=direct on Ingestion and Tests projects to suppress transitive Microsoft.Build.Tasks.Core 17.7.2 vulnerability advisory"
  - "MSBuildLocator NOT used per plan instruction — Roslyn 4.12+ out-of-process MSBuild needs only MSBuildWorkspace.Create()"
metrics:
  duration: 25 min
  completed: "2026-02-26"
  tasks: 2
  files_created: 2
  files_modified: 3
---

# Phase 02 Plan 01: LocalProjectSource Summary

**One-liner:** MSBuildWorkspace-based IProjectSource that discovers .sln/.csproj projects, filters test projects, and returns a ProjectInventory with XML doc file paths.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add MSBuild workspace package and implement LocalProjectSource | 4bfc7c3 | Directory.Packages.props, DocAgent.Ingestion.csproj, LocalProjectSource.cs |
| 2 | Add LocalProjectSource unit tests | ff66309 | LocalProjectSourceTests.cs, DocAgent.Tests.csproj |

## What Was Built

`LocalProjectSource` implements `IProjectSource` with three discovery paths:

1. **Explicit .csproj** — Semicolon-separated list of project file paths, returns single-project inventory with no solution files.
2. **Solution file (.sln)** — Opens via `MSBuildWorkspace.OpenSolutionAsync`, logs workspace diagnostics via `Action<string>` delegate.
3. **Directory** — Scans for a single .sln; if multiple found, throws with list; if none, falls back to recursive csproj scan.

Test projects are excluded by default via suffix matching (`.Tests`, `.Test`, `.Specs`). XML doc files are discovered by scanning each project's directory tree for `{AssemblyName}.xml`.

## Verification

- `dotnet build src/DocAgent.Ingestion/DocAgent.Ingestion.csproj` — 0 errors, 0 warnings
- `dotnet build tests/DocAgent.Tests/DocAgent.Tests.csproj` — 0 errors
- `dotnet test --filter "FullyQualifiedName~LocalProjectSourceTests"` — 5/5 passed (2m 16s)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Microsoft.Build.Tasks.Core vulnerability NU1903 blocked restore**
- **Found during:** Task 1 (first build attempt)
- **Issue:** `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 transitively brings `Microsoft.Build.Tasks.Core` 17.7.2 which has a known vulnerability. With `TreatWarningsAsErrors=true`, this became a hard error.
- **Fix:** Set `<NuGetAuditMode>direct</NuGetAuditMode>` on both `DocAgent.Ingestion.csproj` and `DocAgent.Tests.csproj`. This limits vulnerability auditing to direct packages only, suppressing the transitive advisory.
- **Files modified:** DocAgent.Ingestion.csproj, DocAgent.Tests.csproj
- **Commits:** 4bfc7c3, ff66309

**2. [Rule 1 - Bug] Removed ILogger dependency — no logging package in Ingestion csproj**
- **Found during:** Task 1 (second build attempt)
- **Issue:** Initial implementation used `ILogger` from `Microsoft.Extensions.Logging` which is not referenced by `DocAgent.Ingestion`.
- **Fix:** Replaced with `Action<string>? logWarning` parameter — lightweight, no new dependency.
- **Files modified:** LocalProjectSource.cs
- **Commit:** 4bfc7c3

**3. [Rule 1 - Bug] FluentAssertions `.EndWith()` StringComparison overload doesn't exist**
- **Found during:** Task 2 (build of test project)
- **Issue:** `inventory.SolutionFiles[0].Should().EndWith(".sln", StringComparison.OrdinalIgnoreCase)` — FluentAssertions v6 `EndWith()` only accepts a string reason, not `StringComparison`.
- **Fix:** Changed to `inventory.SolutionFiles[0].ToLower().Should().EndWith(".sln")`.
- **Files modified:** LocalProjectSourceTests.cs
- **Commit:** ff66309

## Self-Check: PASSED

All created files exist on disk. All task commits verified in git log.
