# Phase 14: Solution Ingestion Pipeline - Research

**Researched:** 2026-03-01
**Domain:** Roslyn MSBuildWorkspace solution ingestion, MCP tool surface, partial-success result patterns
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Error & partial success behavior**
- Skip and continue: if a project fails to compile, skip it and ingest everything else; return a partial snapshot with a list of skipped projects and reasons
- Structured per-project status in response: each project gets `{name, status: 'ok'|'skipped'|'failed', reason?, nodeCount?}` — machine-readable
- Any project is enough: even 1 out of N projects succeeding produces a valid partial snapshot
- Surface MSBuild diagnostic warnings in the tool response so agents know about potential issues (missing optional refs, deprecated APIs)

**Non-C# project handling**
- Skip non-C# projects with warning — include them in response as 'skipped (unsupported language)', don't attempt parsing
- All non-C# projects treated uniformly (F#, VB, C++/CLI — no special cases)
- Always ingest test projects — agents might want to query test structure and coverage
- Preserve cross-language dependency edges in ProjectEdge graph — the DAG shows all project references regardless of language, giving agents the full picture

**Multi-targeting & TFM selection**
- Pick the highest/newest TFM (e.g., net10.0 over net48) — most APIs available, matches primary target
- Record the chosen TFM in snapshot metadata so agents know which framework view they're seeing
- No TFM fallback: if the chosen TFM doesn't compile, treat the project as failed (consistent with error handling above)
- Conditional compilation resolves naturally via Roslyn for the chosen TFM — #if branches produce a TFM-specific view

**Ingestion tool response shape**
- Structured summary on success: solution name, project count, total nodes/edges, per-project status array, warnings list, snapshot ID/path
- Require explicit `.sln` path — no directory auto-discovery (avoids ambiguity with multiple .sln files)
- Auto-persist to SnapshotStore like existing `ingest_project` — consistent behavior, agents get a snapshot ID back
- PathAllowlist check on the `.sln` path only — if the .sln is allowed, all projects within it are implicitly allowed

### Claude's Discretion
- MSBuildWorkspace initialization and lifecycle management
- Internal batching/parallelization of project compilation
- TFM parsing and comparison logic
- Warning severity classification
- SnapshotStore key format for solution-level snapshots

### Deferred Ideas (OUT OF SCOPE)
- None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INGEST-01 | Agent can ingest an entire .sln file in one call via `ingest_solution` MCP tool | New `IngestSolution` method on `IngestionTools`; new `ISolutionIngestionService` or extended `IIngestionService`; uses `LocalProjectSource.DiscoverFromSolutionAsync` pattern already in codebase |
| INGEST-02 | Non-C# projects in a solution are skipped gracefully with logged warnings | Roslyn `Project.Language` property returns `"C#"`, `"F#"`, `"Visual Basic"` — filter on this before calling `GetCompilationAsync` |
| INGEST-03 | Multi-targeting projects (e.g. net10.0;net48) are deduplicated to a single TFM | MSBuildWorkspace produces one `Project` entry per TFM; group by `FilePath`, pick highest TFM via version comparison; record chosen TFM in `SymbolGraphSnapshot.SolutionName` or new metadata field |
| INGEST-04 | MSBuildWorkspace load failures are detected and reported (WorkspaceFailed handler, document count validation) | Already patterned in `RoslynSymbolGraphBuilder.ProcessProjectAsync` — `workspace.WorkspaceFailed` event + null compilation check; per-project status object captures reason |
| INGEST-06 | `ingest_solution` is secured with PathAllowlist enforcement (consistent with existing tool security pattern) | Mirror `IngestionTools.IngestProject` security pattern: `_allowlist.IsAllowed(absolutePath)` on the `.sln` path, return same `"access_denied"` opaque error |
</phase_requirements>

---

## Summary

Phase 14 adds the `ingest_solution` MCP tool that ingests an entire `.sln` file in one call. The codebase already contains `LocalProjectSource.OpenSolutionProjectsAsync` which loads a solution and enumerates project file paths. Phase 14 builds on this: it opens the solution, processes each project individually (skip non-C# projects, select the highest TFM for multi-targeted projects, handle MSBuild failures), accumulates per-project `SymbolGraphSnapshot`s into a `SolutionSnapshot`, and returns a structured result with per-project status entries.

The domain has three distinct technical challenges. First, language detection: Roslyn's `Project.Language` property returns `"C#"`, `"F#"`, or `"Visual Basic"` — filtering on this string before calling `GetCompilationAsync` is straightforward and reliable. Second, multi-targeting: when a solution contains a project targeting both `net10.0` and `net48`, `MSBuildWorkspace.OpenSolutionAsync` produces one Roslyn `Project` per TFM. The implementation must group projects by `Project.FilePath`, then pick the entry with the highest TFM using version-comparison on the `Project.ParseOptions.PreprocessorSymbolNames` or `Project.CompilationOptions` properties. Third, PathAllowlist security: the `.sln` path is checked once; all referenced project paths are implicitly allowed since they are inside the solution tree.

The existing `IngestionService` uses a `PipelineOverride` seam and per-path `SemaphoreSlim` for testability and concurrency control. The new solution ingestion path should follow the same seam pattern so tests can inject stub pipelines without triggering real MSBuild. The `SnapshotStore` accepts a `SymbolGraphSnapshot` and returns a content-hashed ID; solution-level storage uses `SolutionSnapshot` which wraps per-project snapshots — the store may need to be extended or the solution snapshot serialized separately.

**Primary recommendation:** Add a dedicated `ISolutionIngestionService` + `SolutionIngestionService` in `DocAgent.McpServer/Ingestion/`, add `IngestSolution` method to `IngestionTools`, and extend `RoslynSymbolGraphBuilder.ProcessProjectAsync` to accept a project-name parameter for `ProjectOrigin` population.

---

## Standard Stack

### Core (already in use — no new packages needed)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 4.12.0 | `MSBuildWorkspace.OpenSolutionAsync`, `Project.Language`, TFM detection | Official Roslyn MSBuild integration |
| `Microsoft.Build.Locator` | (transitive) | `MSBuildLocator.RegisterDefaults()` | Required before any MSBuild type is loaded |
| `MessagePack` | 3.1.4 | `SolutionSnapshot` serialization via `ContractlessStandardResolver` | Existing store format |
| `ModelContextProtocol` | 1.0.0 | `[McpServerTool]` attribute, `McpServer` type, progress notifications | Existing MCP SDK |

### No New Packages

All required libraries are already referenced in `Directory.Packages.props`. Phase 14 is a pure implementation task over existing infrastructure.

---

## Architecture Patterns

### Recommended New Files

```
src/DocAgent.McpServer/
└── Ingestion/
    ├── ISolutionIngestionService.cs   # new interface
    ├── SolutionIngestionService.cs    # new implementation
    └── SolutionIngestionResult.cs     # new result record

src/DocAgent.McpServer/
└── Tools/
    └── IngestionTools.cs              # add IngestSolution method (existing file)

tests/DocAgent.Tests/
└── SolutionIngestionServiceTests.cs   # new tests
└── SolutionIngestionToolTests.cs      # new tests
```

### Pattern 1: Per-Project Status Object

The tool response must include machine-readable per-project status. Define a record in `SolutionIngestionResult`:

```csharp
// New in DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs
public sealed record ProjectIngestionStatus(
    string Name,
    string FilePath,
    string Status,          // "ok" | "skipped" | "failed"
    string? Reason,         // null when ok
    int? NodeCount,         // null when skipped/failed
    string? ChosenTfm);     // null when skipped/failed

public sealed record SolutionIngestionResult(
    string SnapshotId,
    string SolutionName,
    int TotalProjectCount,
    int IngestedProjectCount,
    int TotalNodeCount,
    TimeSpan Duration,
    IReadOnlyList<ProjectIngestionStatus> Projects,
    IReadOnlyList<string> Warnings);
```

### Pattern 2: Language Filtering via Roslyn

Roslyn `Project.Language` is the canonical way to detect project language after `OpenSolutionAsync`:

```csharp
// Source: Roslyn Project type (Microsoft.CodeAnalysis.Workspaces.MSBuild 4.12.0)
// project.Language returns LanguageNames.CSharp ("C#"), LanguageNames.FSharp ("F#"),
// LanguageNames.VisualBasic ("Visual Basic"), etc.

if (project.Language != LanguageNames.CSharp)
{
    statuses.Add(new ProjectIngestionStatus(
        Name: project.Name,
        FilePath: project.FilePath ?? "",
        Status: "skipped",
        Reason: $"Unsupported language: {project.Language}",
        NodeCount: null,
        ChosenTfm: null));
    warnings.Add($"Skipped non-C# project '{project.Name}' (language: {project.Language})");
    continue;
}
```

**Confidence:** HIGH — `LanguageNames.CSharp` is a stable constant in `Microsoft.CodeAnalysis`.

### Pattern 3: Multi-Targeting TFM Selection

When `MSBuildWorkspace.OpenSolutionAsync` loads a multi-targeted project (`<TargetFrameworks>net10.0;net48</TargetFrameworks>`), it produces one Roslyn `Project` per TFM. These share the same `Project.FilePath` but have different `Project.Id` values. Group by file path, then pick the TFM with the highest version:

```csharp
// Group projects by file path to detect multi-targeted duplicates
var projectsByFile = solution.Projects
    .GroupBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase);

foreach (var group in projectsByFile)
{
    // Pick the entry whose TFM has the highest version
    var chosen = group
        .OrderByDescending(p => ExtractTfmVersion(p), TfmVersionComparer.Instance)
        .First();

    // Process only 'chosen'
}

// TFM is embedded in Project.Name for multi-targeted projects:
// "MyLib (net10.0)" and "MyLib (net48)" — parse from Name when FilePath is duplicated
private static string ExtractTfmFromProjectName(Project project)
{
    // Multi-targeted projects have names like "ProjectName (net10.0)"
    var name = project.Name;
    var parenStart = name.LastIndexOf('(');
    var parenEnd = name.LastIndexOf(')');
    if (parenStart >= 0 && parenEnd > parenStart)
        return name.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
    return string.Empty;
}
```

**Confidence:** MEDIUM — the name-based TFM parsing is the observed Roslyn behavior for multi-targeted projects. The TFM ordering logic (newest = highest) is straightforward to implement via `NuGetVersion` parsing or simple string comparison of the numeric suffix.

**TFM version ordering:** Parse the moniker's version component. `net10.0` > `net8.0` > `net6.0` > `net48` (treat legacy `net4x` as lower than any `netX.Y` modern TFM).

### Pattern 4: PathAllowlist — Single Check on .sln Path

Mirror the existing `ingest_project` security gate exactly. Check only the `.sln` path:

```csharp
// In IngestionTools.IngestSolution — mirror of existing IngestProject pattern
var absolutePath = Path.GetFullPath(path);
if (!_allowlist.IsAllowed(absolutePath))
{
    _logger.LogWarning("Solution ingestion denied: path {Path} outside allowlist", absolutePath);
    return ErrorJson("access_denied", "Path is not in the configured allow list.");
}
```

No secondary checks on individual `.csproj` paths within the solution. The `.sln` being allowed implicitly allows its projects (decided in CONTEXT.md).

### Pattern 5: ProjectOrigin Population

`SymbolNode.ProjectOrigin` is currently `null` for all nodes (existing `RoslynSymbolGraphBuilder` does not set it). Phase 14 requires every node produced from a solution ingestion to carry the originating project name.

The existing `ProcessProjectAsync` method needs a `projectName` parameter passed through to `CreateNamespaceNode` and `CreateSymbolNode`:

```csharp
// In RoslynSymbolGraphBuilder — modify CreateSymbolNode signature
private SymbolNode CreateSymbolNode(
    ISymbol symbol,
    IReadOnlyDictionary<string, string> docXmlById,
    string? projectOrigin = null)   // NEW parameter
{
    var id = GetSymbolId(symbol);
    var (returnType, parameters, genericConstraints) = ExtractSignatureFields(symbol);
    return new SymbolNode(
        Id: id,
        Kind: MapKind(symbol),
        DisplayName: symbol.Name,
        FullyQualifiedName: symbol.ToDisplayString(),
        PreviousIds: Array.Empty<SymbolId>(),
        Accessibility: MapAccessibility(symbol.DeclaredAccessibility),
        Docs: ResolveDoc(symbol, docXmlById),
        Span: ExtractSpan(symbol),
        ReturnType: returnType,
        Parameters: parameters,
        GenericConstraints: genericConstraints,
        ProjectOrigin: projectOrigin);   // SET HERE
}
```

Alternatively, a post-processing `with` expression after node collection is simpler and avoids changing deep call chains:

```csharp
// Post-process: stamp all nodes with project origin
var stampedNodes = projectNodes
    .Select(n => n with { ProjectOrigin = projectName })
    .ToList();
```

**Recommendation:** Use the post-processing `with` approach — zero changes to deep node-creation methods.

### Pattern 6: SolutionSnapshot Storage

`SolutionSnapshot` (defined in `DocAgent.Core/SolutionTypes.cs`) holds `IReadOnlyList<SymbolGraphSnapshot> ProjectSnapshots`. The existing `SnapshotStore` stores `SymbolGraphSnapshot` objects. For solution-level storage, two options:

1. Store a single merged `SymbolGraphSnapshot` where all nodes from all projects are combined (consistent with existing `SnapshotStore`)
2. Store a `SolutionSnapshot` in a separate store

**Recommendation per CONTEXT.md and STATE.md decisions:** The existing architecture preserves "single flat snapshot model" with `ProjectOrigin` as the discriminator. Store as a single flat `SymbolGraphSnapshot` containing all project nodes (with `ProjectOrigin` populated). Set `SolutionName` on the snapshot. This is consistent with the existing store and index pipeline.

### Pattern 7: MSBuildWorkspace Lifecycle

The existing code creates one `MSBuildWorkspace` per project in `ProcessProjectAsync`. For solution ingestion, one workspace is more efficient since `OpenSolutionAsync` loads all projects in one call:

```csharp
// Preferred: one workspace for the entire solution
using var workspace = MSBuildWorkspace.Create();
workspace.WorkspaceFailed += (_, args) =>
    warnings.Add($"MSBuildWorkspace [{args.Diagnostic.Kind}]: {args.Diagnostic.Message}");

var solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: ct);

// Then iterate solution.Projects
```

One workspace for the solution is more memory-efficient than N workspaces (MSBuildWorkspace caches MSBuild state) and reflects how Roslyn tools like Roslyn SDK samples use it.

### Anti-Patterns to Avoid

- **Opening the solution N times (once per project):** Use `OpenSolutionAsync` once, then iterate `solution.Projects`.
- **Checking allowlist on each .csproj:** One check on the `.sln` path per CONTEXT.md decision.
- **Merging per-project `SymbolNode` lists before stamping `ProjectOrigin`:** Stamp origin immediately after collecting per-project nodes to preserve mapping.
- **Parallel project processing without understanding MSBuildWorkspace thread safety:** `MSBuildWorkspace` is not thread-safe; process projects sequentially within a single workspace. External semaphore from `IngestionService` covers cross-call concurrency.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting project language | String-match on file extension | `Project.Language` from Roslyn | Handles unusual project types correctly |
| Parsing `.sln` file format | Custom `.sln` parser | `MSBuildWorkspace.OpenSolutionAsync` | `.sln` format is complex and has two variants (`.sln` and `.slnx`) |
| TFM ordering | String sort | NuGetVersion parsing or parse-from-moniker | Edge cases: `net10.0` > `net9.0` fails with lexicographic sort |
| Path security | Custom path normalization | Existing `PathAllowlist.IsAllowed` | Already handles traversal normalization |
| Snapshot persistence | New serialization format | Existing `SnapshotStore.SaveAsync` | Content-hash deduplication, manifest management already implemented |

**Key insight:** The solution loading, workspace, and security infrastructure already exists. Phase 14 is primarily orchestration code wiring together existing pieces with new partial-success and filtering logic.

---

## Common Pitfalls

### Pitfall 1: Lexicographic TFM Version Comparison

**What goes wrong:** Sorting TFM strings lexicographically causes `net9.0` > `net10.0` because `"9"` > `"1"`.
**Why it happens:** Treating the version component as a plain string.
**How to avoid:** Parse the TFM version number as an integer or use `NuGetVersion` parsing. For monikers like `net10.0`, split on `.` after stripping the prefix (`net`), parse the major version as int.
**Warning signs:** A test with `net10.0;net9.0` selects `net9.0`.

### Pitfall 2: MSBuildLocator Not Called Before Workspace Creation

**What goes wrong:** `MSBuildWorkspace.Create()` throws `InvalidOperationException` or fails to find build targets.
**Why it happens:** MSBuild must be located before any MSBuild type is loaded.
**How to avoid:** `MSBuildLocator.RegisterDefaults()` is already called in `Program.cs` before the host starts. No additional calls needed in `SolutionIngestionService`.
**Warning signs:** `MSBuildWorkspace` warnings about missing SDKs at runtime.

### Pitfall 3: Null `Project.FilePath` for In-Memory Projects

**What goes wrong:** `solution.Projects` can include in-memory/synthetic projects where `FilePath` is `null`.
**Why it happens:** Solution files sometimes reference diagnostic projects or misconfigured entries.
**How to avoid:** Filter out `project.FilePath is null` and add to skipped list with reason `"No file path"`.
**Warning signs:** `NullReferenceException` in file-path grouping code.

### Pitfall 4: Multi-Targeted Project Name Parsing Fragility

**What goes wrong:** Assuming project names always follow `"Name (tfm)"` pattern.
**Why it happens:** The parenthesized TFM suffix is Roslyn's convention, not a guarantee.
**How to avoid:** Fall back to treating the project as a single-TFM project if the pattern doesn't match (no parenthesis). Only group-deduplicate when multiple `Project` entries share the same `FilePath`.
**Warning signs:** A multi-targeted project produces duplicate nodes.

### Pitfall 5: Returning Error When Even 1 Project Succeeds

**What goes wrong:** Returning a top-level error JSON when some projects fail, even when others succeeded.
**Why it happens:** Treating any failure as a total failure.
**How to avoid:** Per CONTEXT.md: any project succeeding produces a valid partial snapshot. Only return top-level error when NO projects succeed AND the .sln itself cannot be opened.
**Warning signs:** Agent gets no snapshot ID even though 5 of 6 projects compiled.

### Pitfall 6: WorkspaceFailed Events Not Captured Per-Project

**What goes wrong:** `WorkspaceFailed` fires for all projects; attribution to a specific project is lost.
**Why it happens:** The event fires on the workspace object, not per-project.
**How to avoid:** Correlate warnings to the project currently being processed using a local `List<string>` populated during that project's processing window. Since processing is sequential, warnings in the window belong to the current project.
**Warning signs:** Warnings reference wrong project names in the status array.

---

## Code Examples

### Opening a Solution and Iterating Projects

```csharp
// Source: Pattern derived from existing LocalProjectSource.OpenSolutionProjectsAsync
// (C:\Development\CSharpDocAgentFrameworkMCP\src\DocAgent.Ingestion\LocalProjectSource.cs:136)

using var workspace = MSBuildWorkspace.Create();
workspace.WorkspaceFailed += (_, args) =>
    warnings.Add($"MSBuildWorkspace [{args.Diagnostic.Kind}]: {args.Diagnostic.Message}");

Solution solution;
try
{
    solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: ct);
}
catch (Exception ex)
{
    // .sln itself failed to open — total failure
    return new SolutionIngestionResult(/* ... totalFailure */);
}

var solutionName = Path.GetFileNameWithoutExtension(slnPath);
```

### Grouping Multi-Targeted Projects

```csharp
// Group by FilePath to detect multi-targeted duplicates
var projectGroups = solution.Projects
    .Where(p => p.FilePath is not null)
    .GroupBy(p => p.FilePath!, StringComparer.OrdinalIgnoreCase);

foreach (var group in projectGroups)
{
    // Pick the project entry with the newest TFM
    var chosen = group.Count() == 1
        ? group.First()
        : group.OrderByDescending(p => ExtractTfmVersion(p.Name)).First();

    await ProcessChosenProjectAsync(chosen, statuses, allNodes, allEdges, warnings, ct);
}

// TFM version extraction from project name like "MyLib (net10.0)"
private static Version ExtractTfmVersion(string projectName)
{
    var match = System.Text.RegularExpressions.Regex.Match(
        projectName, @"\(net(\d+)\.(\d+)\)$");
    if (match.Success &&
        int.TryParse(match.Groups[1].Value, out var major) &&
        int.TryParse(match.Groups[2].Value, out var minor))
    {
        return new Version(major, minor);
    }
    // Legacy net4x monikers sort below modern netX.Y
    var legacyMatch = System.Text.RegularExpressions.Regex.Match(
        projectName, @"\(net(\d+)\)$");
    if (legacyMatch.Success && int.TryParse(legacyMatch.Groups[1].Value, out var legacyVer))
        return new Version(0, legacyVer); // 0.48x < 1.0 (modern)
    return new Version(0, 0);
}
```

### Stamping ProjectOrigin via Post-Processing

```csharp
// Post-process collected nodes to stamp ProjectOrigin
// Source: Pattern consistent with existing 'with' expressions in IngestionService.cs
var stampedNodes = projectNodes
    .Select(n => n with { ProjectOrigin = projectName })
    .ToList();

allNodes.AddRange(stampedNodes);
```

### Tool Method Skeleton for ingest_solution

```csharp
// In IngestionTools.cs — new method following ingest_project pattern
[McpServerTool(Name = "ingest_solution")]
[Description("Ingest an entire .NET solution (.sln), building a queryable symbol graph across all C# projects.")]
public async Task<string> IngestSolution(
    ModelContextProtocol.Server.McpServer mcpServer,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Absolute path to .sln file")] string path,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(path))
        return ErrorJson("invalid_input", "path is required.");

    var absolutePath = Path.GetFullPath(path);
    if (!_allowlist.IsAllowed(absolutePath))
    {
        _logger.LogWarning("Solution ingestion denied: {Path} outside allowlist", absolutePath);
        return ErrorJson("access_denied", "Path is not in the configured allow list.");
    }

    // ... progress token extraction (same as IngestProject) ...

    try
    {
        var result = await _solutionIngestionService.IngestAsync(
            absolutePath, progressCallback, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            snapshotId = result.SnapshotId,
            solutionName = result.SolutionName,
            totalProjectCount = result.TotalProjectCount,
            ingestedProjectCount = result.IngestedProjectCount,
            totalNodeCount = result.TotalNodeCount,
            durationMs = result.Duration.TotalMilliseconds,
            projects = result.Projects,
            warnings = result.Warnings,
        }, s_jsonOptions);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Solution ingestion failed for {Path}", absolutePath);
        return ErrorJson("ingestion_failed", ex.Message);
    }
}
```

### Test Seam Pattern (PipelineOverride equivalent)

```csharp
// ISolutionIngestionService with testability seam — mirrors IngestionService
public sealed class SolutionIngestionService : ISolutionIngestionService
{
    // Injectable for tests — avoids real MSBuild in unit tests
    internal Func<string, List<string>, CancellationToken, Task<SolutionIngestionResult>>? PipelineOverride { get; set; }

    public async Task<SolutionIngestionResult> IngestAsync(
        string slnPath,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken ct)
    {
        if (PipelineOverride is not null)
            return await PipelineOverride(slnPath, _warnings, ct);

        // Real implementation
        // ...
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|-----------------|--------|
| One `MSBuildWorkspace` per project (existing `RoslynSymbolGraphBuilder`) | One workspace for entire solution (Phase 14) | Better MSBuild cache reuse; fewer subprocess launches |
| `ProjectOrigin = null` on all nodes (pre-Phase 14) | `ProjectOrigin` = project name on every node | Agents can filter by project |

**No deprecated patterns in this domain for this phase.**

---

## Open Questions

1. **SolutionSnapshot storage — flat vs. structured**
   - What we know: CONTEXT.md says "auto-persist to SnapshotStore like existing `ingest_project`"; STATE.md says "Single flat snapshot model preserved"; `SolutionSnapshot` type exists in `DocAgent.Core` but `SnapshotStore` only handles `SymbolGraphSnapshot`
   - What's unclear: Should a `SolutionSnapshot` be separately serialized, or should we produce a flat `SymbolGraphSnapshot` with all nodes merged (and `SolutionName` set on it)?
   - Recommendation: Produce a flat merged `SymbolGraphSnapshot` (all nodes from all projects, each with `ProjectOrigin` set, `SolutionName` field populated). This is consistent with existing store/index pipeline. The `SolutionSnapshot` type can be used as the assembly/coordination structure internally but the persisted artifact is a flat `SymbolGraphSnapshot`. This is unambiguous given STATE.md's "Single flat snapshot model preserved" decision.

2. **TFM metadata field for "chosen TFM"**
   - What we know: CONTEXT.md says "Record the chosen TFM in snapshot metadata". `SymbolGraphSnapshot` has `IngestionMetadata?` and `SolutionName?` fields but no dedicated TFM field.
   - What's unclear: Where exactly to record the per-project chosen TFM.
   - Recommendation: Record chosen TFM in the `ProjectIngestionStatus.ChosenTfm` field of the tool response. For snapshot-level metadata, store the dominant TFM (most common among ingested projects) in a new `IngestionMetadata` extension or embed in `SolutionName` as `"SolutionName [net10.0]"`. Simplest: record per-project TFMs in the response object only (agents see it there). This is Claude's Discretion territory.

3. **`RoslynSymbolGraphBuilder` modification scope**
   - What we know: Existing builder has `ProcessProjectAsync` that takes a project file path string.
   - What's unclear: Whether to modify the existing builder or create a new solution-aware builder.
   - Recommendation: Do NOT modify `RoslynSymbolGraphBuilder` signature. Create a new `SolutionRoslynProcessor` class in `DocAgent.Ingestion` that takes a Roslyn `Project` object directly (not a file path), to avoid reopening the workspace. Use post-processing `with` expressions to stamp `ProjectOrigin`. Keep existing builder for single-project ingestion.

---

## Sources

### Primary (HIGH confidence)
- Roslyn `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 — `MSBuildWorkspace.OpenSolutionAsync`, `Project.Language`, `LanguageNames.CSharp` — verified against existing usage in `LocalProjectSource.cs` and `RoslynSymbolGraphBuilder.cs`
- Existing codebase — `PathAllowlist.IsAllowed`, `IngestionTools.IngestProject`, `IngestionService`, `SnapshotStore`, `SolutionSnapshot`, `SymbolNode.ProjectOrigin` — all read directly from source

### Secondary (MEDIUM confidence)
- Multi-targeting TFM name pattern `"ProjectName (net10.0)"` — observed in Roslyn tooling; name-based TFM parsing is common in .NET toolchain code

### Tertiary (LOW confidence)
- TFM ordering heuristic (`net10.0 > net48` via version comparison) — established .NET convention, but exact Roslyn behavior for TFM ordering not verified with Context7

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in use; no new packages needed
- Architecture patterns: HIGH — existing codebase patterns directly applicable
- Pitfalls: HIGH — derived from actual existing code paths and design decisions
- TFM multi-targeting mechanics: MEDIUM — pattern is well-established but name-based parsing is heuristic

**Research date:** 2026-03-01
**Valid until:** 2026-04-01 (Roslyn 4.12.0 is stable; no fast-moving changes expected)
