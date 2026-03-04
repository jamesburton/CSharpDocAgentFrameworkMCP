# Phase 19: Incremental Solution Re-ingestion - Research

**Researched:** 2026-03-02
**Domain:** .NET incremental ingestion, per-project manifest hashing, topological dependency cascade, stub lifecycle management
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Manifest storage & keys**
- Manifests stored alongside snapshot output (co-located with the data they describe)
- Keys use solution-relative paths (e.g., `src/MyProject/Foo.cs`) to prevent collision per INGEST-03
- Hash covers source files (.cs) AND project-to-project references + target framework
- Manifests reset on snapshot version bump — new version = full re-ingest

**Dependency cascade**
- Full transitive closure: if A→B→C and A changes, both B and C re-ingest
- Re-ingest in topological (dependency) order — leaves first, matching MSBuild build order
- Structural graph changes (project reference added/removed) trigger full solution re-ingestion
- Circular project references are treated as an error with clear diagnostic

**Observability & diagnostics**
- Per-project log line for each skip/re-ingest decision (e.g., "Skipped MyProject (unchanged)")
- Structured result metadata returned alongside the snapshot: which projects skipped, which re-ingested, and why
- Telemetry counters (projects_skipped, projects_reingested) via existing OpenTelemetry/Aspire infra
- Force-full-reingest boolean parameter as escape hatch to bypass incremental logic

**Stub lifecycle**
- Skipped projects preserve and reuse their existing stubs in the graph
- Projects removed from the solution have their stubs and nodes cleaned from the graph
- Projects that move directories treated as remove + add (old path removed, new path fully ingested)
- Byte-identical integrity test: run both full and incremental ingestion, assert identical snapshots (validates INGEST-05)

### Claude's Discretion
- SHA-256 vs other hash algorithm choice
- Manifest file format (JSON, binary, etc.)
- Exact telemetry meter/counter naming
- Internal caching strategy during topological traversal

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INGEST-01 | Solution re-ingestion skips unchanged projects (per-project SHA-256 manifest comparison) | `IncrementalIngestionEngine` + `FileHashManifest` already implement per-project SHA-256 hashing; we extend that pattern to `SolutionIngestionService` |
| INGEST-02 | Dependency cascade marks downstream projects dirty when their dependencies change | `SolutionSnapshot.ProjectDependencies` (ProjectEdge DAG) already exists; we need topological sort + dirty propagation before the per-project loop |
| INGEST-03 | Per-project manifests use path-based keys to prevent collision | `FileHasher.ComputeManifestAsync` already hashes by absolute file path; we store one manifest per project using solution-relative path as the filename key |
| INGEST-04 | Stub nodes from prior ingestions are correctly regenerated, not accumulated | Current `SolutionIngestionService` re-generates stubs from scratch every run; incremental version must re-use stubs from skipped projects' prior output and drop stubs from removed projects |
| INGEST-05 | Incremental solution result is byte-identical to full re-ingestion for unchanged input | `DeterminismTests` pattern (fix CreatedAt + RunId, compare MessagePack bytes) extends naturally; we add a `SolutionIncrementalDeterminismTests` class |
</phase_requirements>

---

## Summary

Phase 19 extends `SolutionIngestionService` — currently a full re-ingest on every call — with per-project incremental logic driven by SHA-256 file manifests, dependency cascade via topological sort, and correct stub lifecycle management. The project already has all the foundational infrastructure needed: `FileHashManifest` / `FileHasher` for SHA-256 hashing, `IncrementalIngestionEngine` for single-project incremental logic, `SolutionSnapshot.ProjectDependencies` for the project DAG, and `DetectCycles` for circular reference detection. The core work is wiring these pieces together inside `SolutionIngestionService.IngestAsync` (or a new `IncrementalSolutionIngestionService` wrapper) and extending `SolutionIngestionResult` to surface skip/reingest metadata.

The highest-risk correctness concern is the stub lifecycle under incremental runs. Stubs are generated during the namespace walk inside `WalkTypeInline` and accumulated in a shared `seenStubIds` set. When only a subset of projects re-ingest, stubs from skipped projects must be restored from the previous snapshot, and stubs from removed projects must be purged. Getting this wrong produces ghost stubs (accumulation, violating INGEST-04) or missing stubs (edges with no target node). The byte-identity test (INGEST-05) will catch both classes of error.

**Primary recommendation:** Implement incremental solution ingestion as a new `IncrementalSolutionIngestionService` class that wraps/replaces `SolutionIngestionService`, keeping the existing class untouched for reference and test stability. Store per-project manifests as `{solutionRelativePath}.manifest.json` files in the snapshot artifacts directory. Use the existing `FileHasher` + `FileHashManifest` types unchanged; only extend the hash input to include project references and TFM alongside `.cs` file hashes.

---

## Standard Stack

### Core
| Library / Type | Location | Purpose | Why Standard |
|----------------|----------|---------|--------------|
| `FileHashManifest` / `FileHasher` | `DocAgent.Ingestion` | SHA-256 per-file manifest, diff, save/load | Already used by `IncrementalIngestionEngine`; proven pattern |
| `SymbolSorter` | `DocAgent.Ingestion` | Deterministic node/edge ordering | Required for byte-identity |
| `SolutionSnapshot.ProjectDependencies` | `DocAgent.Core` | Project DAG (edges already built during ingestion) | Source of truth for dependency cascade |
| `DetectCycles` (private in `SolutionIngestionService`) | `DocAgent.McpServer.Ingestion` | Circular reference detection | Locked decision: cycles = error + diagnostic |
| `OpenTelemetry` via `DocAgentTelemetry.Source` | `DocAgent.McpServer.Telemetry` | Activity tags + counters | Existing infra; locked for telemetry counters |
| xUnit + FluentAssertions | `DocAgent.Tests` | Test framework | Project standard |

### Supporting
| Type | Purpose | When to Use |
|------|---------|-------------|
| `SnapshotStore` | Saves/loads `.msgpack` snapshots | Final snapshot persistence (unchanged) |
| `MessagePackSerializer` | Byte-identical serialization for determinism test | INGEST-05 test |
| `ILogger<T>` | Structured per-project log lines ("Skipped X (unchanged)") | INGEST-01 observability |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| JSON manifest files | MessagePack binary manifest | JSON is human-readable and debuggable; locked as Claude's discretion, recommend JSON |
| SHA-256 | XxHash128 (already used in `SnapshotStore`) | XxHash128 is faster but not cryptographic; for manifests that gate re-ingestion, SHA-256 correctness guarantee is higher; locked as Claude's discretion, recommend SHA-256 |
| Separate `IncrementalSolutionIngestionService` class | Modify `SolutionIngestionService` in place | New class avoids breaking existing tests and keeps a clean implementation boundary |

---

## Architecture Patterns

### Recommended Project Structure

No new files are needed in `DocAgent.Core`. New/modified files:

```
src/DocAgent.McpServer/Ingestion/
├── SolutionIngestionService.cs          # Keep as-is (full re-ingest fallback)
├── IncrementalSolutionIngestionService.cs   # NEW — implements ISolutionIngestionService
├── SolutionIncrementalResult.cs         # NEW or extend SolutionIngestionResult

src/DocAgent.Ingestion/
├── FileHashManifest.cs                  # Extend hash input to include project refs + TFM
├── SolutionManifestStore.cs             # NEW — per-project manifest save/load by solution-relative key

tests/DocAgent.Tests/IncrementalIngestion/
├── SolutionIncrementalIngestionTests.cs # NEW — unit tests using PipelineOverride pattern
├── SolutionIncrementalDeterminismTests.cs  # NEW — INGEST-05 byte-identity test
```

### Pattern 1: Per-Project Manifest Keyed by Solution-Relative Path

**What:** Each project gets its own manifest file stored at `{artifactsDir}/{solutionRelativePath}.manifest.json` where `solutionRelativePath` replaces path separators with `__` (or uses a URL-encoded form) to create a flat filename.

**When to use:** On every `ingest_solution` call, before deciding whether to skip a project.

**Example:**
```csharp
// Key construction — solution-relative path with separator normalization
private static string ManifestFileName(string slnPath, string projectFilePath)
{
    var slnDir = Path.GetDirectoryName(Path.GetFullPath(slnPath))!;
    var relative = Path.GetRelativePath(slnDir, Path.GetFullPath(projectFilePath));
    // Replace path separators so the key is a flat filename
    var safeKey = relative.Replace(Path.DirectorySeparatorChar, '__')
                           .Replace(Path.AltDirectorySeparatorChar, '__');
    return safeKey + ".manifest.json";
}
```

**Hash input per project:** Enumerate `.cs` files in project directory (same as `FileHasher.ComputeManifestAsync`) PLUS a synthetic entry for `__project_refs__` = sorted comma-joined project reference paths, and `__tfm__` = chosen TFM string.

```csharp
// Extend manifest to include structural inputs
private static async Task<FileHashManifest> ComputeProjectManifestAsync(
    string projectFilePath,
    IReadOnlyList<string> projectReferencePaths,
    string? chosenTfm,
    CancellationToken ct)
{
    var csFiles = Directory.EnumerateFiles(
        Path.GetDirectoryName(projectFilePath)!, "*.cs", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();

    var manifest = await FileHasher.ComputeManifestAsync(csFiles, ct);

    // Inject structural inputs as pseudo-file entries
    var hashes = new Dictionary<string, string>(manifest.FileHashes);
    hashes["__project_refs__"] = ComputeStringHash(
        string.Join(",", projectReferencePaths.OrderBy(r => r, StringComparer.Ordinal)));
    hashes["__tfm__"] = ComputeStringHash(chosenTfm ?? string.Empty);

    return manifest with { FileHashes = hashes };
}
```

### Pattern 2: Topological Sort for Dependency Cascade

**What:** Before iterating projects, compute a topological sort of the project DAG (leaves first = dependencies before dependents). Build a dirty set starting from directly-changed projects, then propagate: any project that depends on a dirty project is also dirty.

**When to use:** After computing per-project manifests and before the ingestion loop.

**Example:**
```csharp
// Topological sort using Kahn's algorithm (already have cycle detection)
private static IReadOnlyList<string> TopologicalSort(
    IReadOnlyList<ProjectEntry> projects,
    IReadOnlyList<ProjectEdge> edges)
{
    // Build in-degree map and adjacency list (dependents of each node)
    var inDegree = projects.ToDictionary(p => p.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
    var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var edge in edges)
    {
        // edge.From depends on edge.To, so edge.To must come first
        inDegree[edge.From] = inDegree.GetValueOrDefault(edge.From) + 1;
        if (!dependents.TryGetValue(edge.To, out var list))
        {
            list = new List<string>();
            dependents[edge.To] = list;
        }
        list.Add(edge.From);
    }

    var queue = new Queue<string>(
        inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var sorted = new List<string>();

    while (queue.Count > 0)
    {
        var current = queue.Dequeue();
        sorted.Add(current);
        foreach (var dependent in dependents.GetValueOrDefault(current) ?? [])
        {
            if (--inDegree[dependent] == 0)
                queue.Enqueue(dependent);
        }
    }

    return sorted; // leaves first
}

// Dirty propagation: transitive closure
private static HashSet<string> ComputeDirtySet(
    IReadOnlyList<string> directlyChanged,
    IReadOnlyList<ProjectEdge> edges)
{
    var dirty = new HashSet<string>(directlyChanged, StringComparer.OrdinalIgnoreCase);

    // Build: for each project, which projects depend on it?
    var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var edge in edges)
    {
        if (!dependents.TryGetValue(edge.To, out var list))
        {
            list = [];
            dependents[edge.To] = list;
        }
        list.Add(edge.From);
    }

    // BFS/DFS to propagate dirty marks downstream
    var queue = new Queue<string>(directlyChanged);
    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        foreach (var dep in dependents.GetValueOrDefault(node) ?? [])
        {
            if (dirty.Add(dep))
                queue.Enqueue(dep);
        }
    }

    return dirty;
}
```

### Pattern 3: Structural Change Detection → Full Re-ingest

**What:** Before per-project manifests are compared, check if the project set itself has changed (projects added, removed, or reference structure changed). If yes, force full re-ingestion.

**When to use:** Compare current project file paths from `SolutionSnapshot.Projects` against the newly-loaded Roslyn `solution.Projects`.

**Example:**
```csharp
// Structural change: project set changed (added/removed projects)
private static bool HasStructuralChange(
    IReadOnlyList<ProjectEntry>? previousProjects,
    IReadOnlyList<string> currentProjectPaths)
{
    if (previousProjects is null) return true;
    var prevPaths = new HashSet<string>(
        previousProjects.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);
    var currPaths = new HashSet<string>(currentProjectPaths, StringComparer.OrdinalIgnoreCase);
    return !prevPaths.SetEquals(currPaths);
}
```

The existing `ProjectDependencies` (edges) from the previous `SolutionSnapshot` can be compared to newly-loaded edges to detect reference topology changes.

### Pattern 4: Stub Lifecycle Under Incremental Runs

**What:** Stubs are synthesized as `NodeKind.Stub` nodes by `MaybeAddStubNode` during the namespace walk. Under incremental ingestion:

1. **Skipped projects** → preserve all `NodeKind.Stub` nodes from the previous snapshot that were introduced by those projects. Since stubs have no `Span` and their `ProjectOrigin` is the external assembly name (not a solution project name), they cannot be attributed to a specific solution project. The safe approach: preserve ALL stubs from the previous snapshot and let the re-ingested projects regenerate their stubs. After merging, deduplicate by `SymbolId` (new wins over old).

2. **Re-ingested projects** → regenerate stubs fresh (existing behavior: `seenStubIds` set).

3. **Removed projects** → after removing their `NodeKind.Real` nodes, also remove stubs that are ONLY referenced by edges from removed projects. A stub with no remaining inbound edges (from real nodes) can be pruned.

**Key insight:** The cleanest correct approach is:
- Collect stubs from the previous snapshot
- Run the re-ingestion loop (which regenerates stubs for re-ingested projects, deduped by `seenStubIds`)
- After loop: union previous stubs + new stubs, dedup by `SymbolId`
- Prune stubs with no surviving inbound edges

### Anti-Patterns to Avoid

- **Accumulating stubs across runs without pruning:** Each incremental run that adds new external types will grow the stub set unboundedly. Must prune stubs with no surviving inbound edges.
- **Keying manifests by project name only:** Two projects named `Tests` in different directories collide. Use solution-relative path (INGEST-03, locked decision).
- **Comparing absolute paths across machines:** Manifests stored with absolute paths break when solution is checked out to a different path. The manifest keys (solution-relative) must be path-portable; only the actual file hash matters.
- **Forgetting to reset manifests on schema version bump:** A new `SymbolGraphSnapshot.SchemaVersion` means old manifests describe a different schema — always force full re-ingest when schema version changes.
- **Not sorting projects topologically before the loop:** Ingesting a dependent project before its dependency means the partial snapshot for the dependent may have stale cross-project edge stubs.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SHA-256 file hashing | Custom hash logic | `FileHasher.ComputeManifestAsync` (existing) | Already tested, handles async file reads, produces stable hex strings |
| Topological sort | Custom algorithm | Kahn's algorithm (simple BFS, ~20 lines) | Simple enough to inline; no external dependency needed |
| Cycle detection | Custom DFS | `DetectCycles` (existing private method in `SolutionIngestionService`) | Extract to `internal static` helper; already tested via `SolutionIngestionServiceTests` |
| Manifest serialization | Custom format | `System.Text.Json` + `FileHashManifest` record (existing) | Matches project convention; human-readable for debugging |
| Byte-identity comparison | Custom snapshot comparison | `MessagePackSerializer.Serialize` + `SequenceEqual` | Exact pattern from `DeterminismTests`; already proven |

**Key insight:** The majority of this phase is orchestration — wiring existing pieces (`FileHashManifest`, `FileHasher`, `SolutionSnapshot.ProjectDependencies`, stub generation, `SymbolSorter`) into the solution ingestion loop. Almost no new algorithmic code is needed.

---

## Common Pitfalls

### Pitfall 1: Stub Attribution Gap
**What goes wrong:** Stubs have `Span = null` and `ProjectOrigin` = external assembly name (e.g., `"Microsoft.Extensions.Logging"`), not a solution project name. It is impossible to determine which solution project "owns" a stub by inspecting the stub node alone.
**Why it happens:** Stubs are synthesized for external types referenced by any project; multiple projects may reference the same external type.
**How to avoid:** Preserve ALL stubs from the previous snapshot as a baseline. Let the re-ingestion loop regenerate stubs for re-ingested projects into `seenStubIds`. After merge, union baseline stubs with newly-generated stubs (dedup by ID). Prune stubs with zero surviving inbound edges.
**Warning signs:** Stub count grows monotonically across incremental runs without stabilizing.

### Pitfall 2: Manifest Key Collision on Rename/Move
**What goes wrong:** A project at `src/Foo/Foo.csproj` is moved to `lib/Foo/Foo.csproj`. The old manifest key (`src__Foo__Foo.csproj.manifest.json`) persists but the new key doesn't exist — first run is treated as new project (full re-ingest of that project). The old manifest file is an orphan.
**Why it happens:** Manifest files are keyed by solution-relative path; moving a project changes the key.
**How to avoid:** On each ingestion run, after determining current project paths, delete manifest files whose keys no longer correspond to any current project. This is the "projects that move directories treated as remove + add" decision.
**Warning signs:** Orphan manifest files accumulate in the artifacts directory.

### Pitfall 3: Byte-Identity Broken by Metadata Fields
**What goes wrong:** INGEST-05 test fails because `IngestionMetadata.RunId` (a GUID) or `CreatedAt` timestamps differ between full and incremental runs.
**Why it happens:** `RunId` is `Guid.NewGuid()` and `CreatedAt` is `DateTimeOffset.UtcNow` — both non-deterministic by definition.
**How to avoid:** The determinism test must fix these fields before comparing bytes — same pattern as `DeterminismTests` which uses `FixedTimestamp` and nulls `ContentHash`. For INGEST-05, null or fix `IngestionMetadata` before byte comparison, or compare only `Nodes` and `Edges` collections.
**Warning signs:** Test passes locally but fails in CI due to timing differences.

### Pitfall 4: MSBuildWorkspace Must Still Be Opened Even for Unchanged Solutions
**What goes wrong:** To determine the current project list and dependency structure, `MSBuildWorkspace.OpenSolutionAsync` must still be called on every `ingest_solution` invocation, even when all projects are unchanged. This is the locked "out of scope" item in REQUIREMENTS.md: "Skipping MSBuildWorkspace open for unchanged solutions — Requires DAG cache; out of scope for v1.3".
**Why it happens:** MSBuildWorkspace is the only way to get the current project reference DAG and TFM selection.
**How to avoid:** Accept this cost. Incremental benefit is in skipping per-project `GetCompilationAsync` and namespace walks (the expensive parts), not in skipping workspace open.
**Warning signs:** Attempting to cache the workspace across calls will encounter threading issues (MSBuildWorkspace is not thread-safe) and stale compilation issues.

### Pitfall 5: Cross-Project Edge Staleness After Partial Re-ingest
**What goes wrong:** Project B is re-ingested (changed), but Project A (unchanged, uses types from B) still has edges pointing to old B symbol IDs. If B renamed a type, A's edges now point to non-existent node IDs.
**Why it happens:** Incremental ingestion preserves A's nodes and edges without re-walking A's compilation.
**How to avoid:** This is why the dependency cascade (INGEST-02) marks A dirty when B changes. The topological sort ensures B is re-ingested before A, and since A depends on B, A is in the dirty set and also re-ingested. The cascade eliminates the staleness issue by design.
**Warning signs:** Edges with `From` or `To` IDs that don't match any node in the merged snapshot.

---

## Code Examples

### Full Incremental Solution Ingestion Skeleton

```csharp
// IncrementalSolutionIngestionService.IngestAsync — high-level flow
public async Task<SolutionIngestionResult> IngestAsync(
    string slnPath, Func<int, int, string, Task>? reportProgress, CancellationToken ct)
{
    // 1. Open MSBuildWorkspace (always required to get current project list + DAG)
    using var workspace = MSBuildWorkspace.Create();
    var solution = await workspace.OpenSolutionAsync(slnPath, cancellationToken: ct);

    // 2. Deduplicate multi-targeted projects (existing logic)
    var chosenProjects = DeduplicateMultiTargetedProjects(solution.Projects);

    // 3. Load previous SolutionSnapshot (if any) — source of: previous project list, stubs, DAG
    var previousSnapshot = await LoadPreviousSolutionSnapshotAsync(slnPath, ct);

    // 4. Detect structural change (projects added/removed)
    if (HasStructuralChange(previousSnapshot?.Snapshot?.Projects, chosenProjects))
    {
        return await FullIngestAsync(slnPath, chosenProjects, solution, ...);
    }

    // 5. Compute per-project manifests for all current projects
    var currentManifests = await ComputeAllProjectManifestsAsync(chosenProjects, ct);

    // 6. Load previous per-project manifests
    var previousManifests = await LoadAllProjectManifestsAsync(slnPath, chosenProjects, ct);

    // 7. Determine directly-changed projects (manifest diff)
    var directlyChanged = chosenProjects
        .Where(p => ManifestHasChanged(previousManifests[p.FilePath], currentManifests[p.FilePath]))
        .Select(p => p.Name)
        .ToList();

    // 8. Build project DAG from current solution
    var projectEdges = BuildProjectEdges(chosenProjects, solution);

    // 9. Compute full dirty set (transitive dependency cascade)
    var dirtySet = ComputeDirtySet(directlyChanged, projectEdges);

    // 10. Topological sort — leaves (dependencies) first
    var sortedProjects = TopologicalSort(chosenProjects, projectEdges);

    // 11. Ingest dirty projects in topological order; skip clean ones
    var results = new List<ProjectIngestionResult>();
    var allNodes = new List<SymbolNode>();
    var allEdges = new List<SymbolEdge>();

    foreach (var projectName in sortedProjects)
    {
        if (!dirtySet.Contains(projectName))
        {
            // Skip: preserve nodes/edges from previous snapshot
            _logger.LogInformation("Skipped {Project} (unchanged)", projectName);
            PreserveProjectFromPrevious(projectName, previousSnapshot, allNodes, allEdges);
            results.Add(new ProjectIngestionResult(projectName, skipped: true));
        }
        else
        {
            // Re-ingest
            _logger.LogInformation("Re-ingesting {Project} (changed or dependent changed)", projectName);
            var project = chosenProjects[projectName];
            var compilation = await project.GetCompilationAsync(ct);
            WalkCompilationAndAccumulate(compilation, allNodes, allEdges, ...);
            results.Add(new ProjectIngestionResult(projectName, skipped: false));
        }
    }

    // 12. Merge stubs: preserve prior stubs, add newly-generated stubs, prune orphaned
    MergeStubs(previousSnapshot, allNodes, allEdges, newlyGeneratedStubs);

    // 13. Sort for determinism
    var finalSnapshot = new SymbolGraphSnapshot(
        ...,
        Nodes: SymbolSorter.SortNodes(allNodes),
        Edges: SymbolSorter.SortEdges(allEdges));

    // 14. Save manifests + snapshot
    await SaveAllProjectManifestsAsync(slnPath, currentManifests, ct);
    await _store.SaveAsync(finalSnapshot, ct: ct);

    // 15. Return result with skip/reingest metadata
    return BuildResult(results, finalSnapshot, ...);
}
```

### Telemetry Integration (existing pattern)

```csharp
// Use existing DocAgentTelemetry.Source for activity tags
using var activity = DocAgentTelemetry.Source.StartActivity(
    "solution.incremental_ingest", ActivityKind.Internal);
activity?.SetTag("projects.total", totalCount);
activity?.SetTag("projects.skipped", skippedCount);
activity?.SetTag("projects.reingested", reingestedCount);

// OpenTelemetry counters (new Meter — Claude's discretion on naming)
// Recommended naming: "docagent.ingestion.projects_skipped" / "docagent.ingestion.projects_reingested"
private static readonly Meter s_meter = new("DocAgent.Ingestion");
private static readonly Counter<int> s_projectsSkipped =
    s_meter.CreateCounter<int>("docagent.ingestion.projects_skipped");
private static readonly Counter<int> s_projectsReingested =
    s_meter.CreateCounter<int>("docagent.ingestion.projects_reingested");
```

### INGEST-05 Determinism Test Pattern

```csharp
[Fact]
public async Task IncrementalSolution_ByteIdentical_To_FullIngest_WhenUnchanged()
{
    // Full ingest
    var fullResult = await _service.IngestAsync(slnPath, null, ct);
    var fullSnapshot = await _store.LoadAsync(fullResult.SnapshotId, ct);

    // Incremental ingest (no file changes)
    var incrementalResult = await _incrementalService.IngestAsync(slnPath, null, ct);
    var incrementalSnapshot = await _store.LoadAsync(incrementalResult.SnapshotId, ct);

    // Normalize non-deterministic fields before byte comparison
    var fixed1 = fullSnapshot! with
    {
        CreatedAt = FixedTimestamp,
        ContentHash = null,
        IngestionMetadata = null  // RunId and timestamps are non-deterministic
    };
    var fixed2 = incrementalSnapshot! with
    {
        CreatedAt = FixedTimestamp,
        ContentHash = null,
        IngestionMetadata = null
    };

    var bytes1 = MessagePackSerializer.Serialize(fixed1, ContractlessStandardResolver.Options);
    var bytes2 = MessagePackSerializer.Serialize(fixed2, ContractlessStandardResolver.Options);

    bytes1.SequenceEqual(bytes2).Should().BeTrue(
        "incremental ingest with no file changes must produce byte-identical nodes/edges to full ingest");
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `SolutionIngestionService` full re-ingest every call | `IncrementalSolutionIngestionService` skips unchanged projects | Phase 19 | Eliminates `GetCompilationAsync` cost for unchanged projects — dominant performance bottleneck |
| Single flat file-hash manifest for single-project ingestion | Per-project manifests with solution-relative path keys | Phase 19 | Prevents key collision (INGEST-03), enables per-project skip decisions |
| Stubs always regenerated from scratch | Stubs preserved from prior snapshot for skipped projects + pruned for removed projects | Phase 19 | INGEST-04: no stub accumulation; edges always have valid targets |

**Deprecated/outdated:**
- The global `file-hashes.json` manifest pattern (used by `IncrementalIngestionEngine` for single-project) is NOT reused for solution-level incremental — solution needs one manifest per project, not one manifest for all files.

---

## Open Questions

1. **Where to store per-project manifests?**
   - What we know: Locked decision is "co-located with snapshot output" = `SnapshotStore.ArtifactsDir`
   - What's unclear: Whether to use a flat directory or a `manifests/` subdirectory
   - Recommendation: Use `{artifactsDir}/project-manifests/{safeKey}.manifest.json` to avoid polluting the snapshot directory with manifest files. The `safeKey` = solution-relative path with separators replaced by `__`.

2. **How to load the previous `SolutionSnapshot` to get prior project/stub state?**
   - What we know: `SnapshotStore.ListAsync()` returns all stored snapshots; `SolutionIngestionResult.SnapshotId` is the content hash of the last ingested snapshot
   - What's unclear: There's no "last snapshot for solution X" index in `SnapshotStore`
   - Recommendation: Store a pointer file `{artifactsDir}/latest-{solutionName}.ptr` containing the content hash of the most recent solution snapshot. On incremental ingest, load via `SnapshotStore.LoadAsync(ptr)`. This is analogous to the `file-hashes.json` pattern.

3. **How does `ISolutionIngestionService` surface skip/reingest metadata to callers?**
   - What we know: `SolutionIngestionResult` has `Projects: IReadOnlyList<ProjectIngestionStatus>` with `Status` = "ok" / "skipped" / "failed"
   - What's unclear: `ProjectIngestionStatus.Status = "skipped"` already exists but currently means "skipped due to non-C# language or no file path", not "skipped due to unchanged manifest"
   - Recommendation: Add a new `Reason` value like `"skipped_unchanged"` to distinguish incremental skips from structural skips; add `ProjectsSkipped` and `ProjectsReingested` counts to `SolutionIngestionResult`.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.x + FluentAssertions |
| Config file | None (convention-based discovery) |
| Quick run command | `dotnet test --filter "FullyQualifiedName~IncrementalSolution"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INGEST-01 | Second call with no file changes makes zero `GetCompilationAsync` calls | unit | `dotnet test --filter "FullyQualifiedName~SolutionIncrementalIngestionTests"` | ❌ Wave 0 |
| INGEST-02 | Changing project A marks its dependents dirty and re-ingests them | unit | `dotnet test --filter "FullyQualifiedName~SolutionIncrementalIngestionTests.DependencyDirtyPropagation"` | ❌ Wave 0 |
| INGEST-03 | Two projects with the same name in different dirs produce non-colliding manifests | unit | `dotnet test --filter "FullyQualifiedName~SolutionIncrementalIngestionTests.ManifestKeyCollision"` | ❌ Wave 0 |
| INGEST-04 | Stubs from skipped projects preserved; stubs from removed projects pruned | unit | `dotnet test --filter "FullyQualifiedName~SolutionIncrementalIngestionTests.StubLifecycle"` | ❌ Wave 0 |
| INGEST-05 | Incremental result byte-identical to full re-ingest when nothing changed | unit | `dotnet test --filter "FullyQualifiedName~SolutionIncrementalDeterminismTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~IncrementalSolution"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/IncrementalIngestion/SolutionIncrementalIngestionTests.cs` — covers INGEST-01, INGEST-02, INGEST-03, INGEST-04
- [ ] `tests/DocAgent.Tests/IncrementalIngestion/SolutionIncrementalDeterminismTests.cs` — covers INGEST-05
- [ ] `tests/DocAgent.Tests/IncrementalIngestion/SolutionManifestStoreTests.cs` — covers manifest key construction and collision avoidance

---

## Sources

### Primary (HIGH confidence)
- Codebase analysis (direct file read) — `DocAgent.Ingestion/FileHashManifest.cs`, `IncrementalIngestionEngine.cs`, `DocAgent.McpServer/Ingestion/SolutionIngestionService.cs`, `DocAgent.Core/SolutionTypes.cs`, `DeterminismTests.cs`, `IncrementalIngestionEngineTests.cs`
- Project REQUIREMENTS.md — INGEST-01 through INGEST-05 definitions
- Phase CONTEXT.md — all locked decisions

### Secondary (MEDIUM confidence)
- Kahn's algorithm for topological sort — standard computer science, no external library needed, well-understood O(V+E) BFS

### Tertiary (LOW confidence)
- None — all claims are grounded in direct codebase inspection or locked decisions

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries/types verified by direct code reading
- Architecture patterns: HIGH — patterns derived from existing `IncrementalIngestionEngine` + `SolutionIngestionService` code
- Pitfalls: HIGH — derived from reading the actual implementation and identifying edge cases in the locked decisions

**Research date:** 2026-03-02
**Valid until:** 2026-04-02 (stable domain — .NET APIs and project patterns)
