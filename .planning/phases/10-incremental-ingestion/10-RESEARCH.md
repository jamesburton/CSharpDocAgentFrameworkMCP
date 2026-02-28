# Phase 10: Incremental Ingestion - Research

**Researched:** 2026-02-28
**Domain:** .NET / C# incremental file change detection and partial snapshot rebuilding
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Change detection strategy
- Content hash comparison (SHA-256) for definitive change detection — immune to timestamp drift
- Persistent manifest (file → hash mapping) stored alongside the snapshot for fast comparison on subsequent runs
- Deleted files: symbols removed from the new snapshot (clean removal, no tombstoning)
- No dry-run mode for now — keep it simple, can be added later

#### Merge & preservation
- New immutable snapshot built each run — combine unchanged symbols from previous + newly parsed symbols from changed files
- Edges recomputed only for changed files — edges between two unchanged files preserved as-is
- Previous snapshot retained for diffing (aligns with Phase 9's SymbolGraphDiffer)
- Partial classes/methods: if any file containing a partial type changes, re-parse ALL files containing that partial type to ensure completeness

#### Change tracking metadata
- File-level change log per ingestion run: which files were added/modified/removed, plus which symbols were affected per file
- Metadata embedded in SymbolGraphSnapshot (IngestionMetadata property) — travels with the snapshot
- Each ingestion run gets a GUID + timestamp for traceability
- Internal only for now — no MCP tool exposure until a future phase requires it

#### Correctness guarantees
- Hard invariant: incremental snapshot must be identical to full re-ingestion (per roadmap success criteria)
- Test-time verification: tests run both paths and compare — production trusts the incremental path
- Fallback to full re-ingestion if incremental detects an inconsistency it can't handle
- Force-full option on the ingestion API as escape hatch for periodic full refreshes

### Claude's Discretion
- Hash algorithm implementation details (streaming vs full-read)
- Manifest storage format (JSON, binary, etc.)
- Internal caching strategy during merge
- Edge recomputation algorithm specifics
- Exact IngestionMetadata type shape (beyond the required fields)

### Deferred Ideas (OUT OF SCOPE)
- MCP tool for querying change history ("what changed since last run?") — future phase
- Dry-run / impact preview mode — future enhancement
- Filesystem watcher for real-time change detection — separate phase
- Symbol-level diff in metadata (overlaps with Phase 9's SymbolGraphDiffer) — not needed here
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| R-INCR-INGEST | File change detection and partial re-ingestion — only re-process changed files, produce snapshot identical to full re-ingestion, with change tracking metadata | File manifest pattern, SHA-256 hashing of source files, symbol-partition-by-source-file merge, IngestionMetadata domain type, correctness test pattern |
</phase_requirements>

---

## Summary

Phase 10 adds incremental ingestion to the existing `RoslynSymbolGraphBuilder` / `IngestionService` pipeline. The core idea: on each run, hash every source `.cs` file, compare against a persisted file manifest from the previous run, and only invoke Roslyn on files that changed. Symbols from unchanged files are carried forward from the previous `SymbolGraphSnapshot`. The result is a fresh `SymbolGraphSnapshot` that must be byte-identical to a full re-ingestion on the same files.

The existing codebase already has the right seams. `SymbolGraphSnapshot` is an immutable record — combining nodes/edges from a previous snapshot with newly-parsed nodes/edges is straightforward set merge. `SnapshotStore` already stores the manifest.json for snapshot history; a parallel `FileHashManifest` (file path → SHA-256 hex, stored in the artifacts directory) provides the incremental state between runs. `RoslynSymbolGraphBuilder.BuildAsync` processes one project file at a time in a foreach loop — this boundary naturally maps to the "which project files changed" decision.

The hardest problem is partial classes: a `partial class Foo` spread across `Foo.cs` and `Foo.Designer.cs` means changing either file requires re-parsing both. The solution is to build a "partial type file set" index from the previous snapshot's `SourceSpan` data — if any file in a partial group changes, treat all files in the group as changed. This is C#-specific and must be handled before the incremental vs. full-reparse decision.

**Primary recommendation:** Implement `IncrementalIngestionEngine` in `DocAgent.Ingestion` as a wrapper over `RoslynSymbolGraphBuilder`. It owns the file manifest, change detection, partial-class grouping, and symbol merge. `IngestionService` gets a `forceFullReingestion` escape hatch. `IngestionMetadata` is a new Core type embedded in `SymbolGraphSnapshot`.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `System.Security.Cryptography.SHA256` | .NET 10 BCL | File content hashing | Already used in codebase (ComputeSourceFingerprint uses SHA256.HashData); no external dep |
| `System.Text.Json` | .NET 10 BCL | File manifest serialization | Already used in SnapshotStore for manifest.json; consistent pattern |
| `MessagePack` (ContractlessStandardResolver) | 3.1.4 (already in Directory.Packages.props) | Snapshot serialization | Already the project's snapshot format; IngestionMetadata serializes the same way |
| `Microsoft.CodeAnalysis.CSharp` | 4.12.0 (already pinned) | Re-parse changed project files | Already the builder's Roslyn dependency |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.IO.Hashing.XxHash128` | .NET 10 BCL | Snapshot content hash (existing pattern) | Already used in SnapshotStore.SaveAsync — do NOT use for file hashing (use SHA-256 per decision) |
| xUnit + FluentAssertions | Already in test project | Correctness tests comparing incremental vs full | All new tests follow this pattern |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SHA-256 for file hashing | MD5 / XxHash | Decision locked: SHA-256 — immune to timestamp drift, collision-resistant |
| JSON for file manifest | MessagePack / SQLite | JSON is readable, debuggable, consistent with manifest.json precedent — good default |
| Per-project-file granularity | Per-source-file granularity | Project-file granularity is simpler and matches RoslynSymbolGraphBuilder's existing loop; source-file granularity would require Roslyn SyntaxTree-level manipulation which is significantly more complex |

**Installation:** No new packages needed — all dependencies already present.

---

## Architecture Patterns

### Recommended Project Structure

```
src/DocAgent.Core/
└── IngestionMetadata.cs          # New: IngestionMetadata record + FileChangeRecord + ChangeKind enum

src/DocAgent.Ingestion/
├── FileHashManifest.cs           # New: persisted file→hash map; load/save/diff logic
├── IncrementalIngestionEngine.cs # New: orchestrates change detection, partial-class resolution, merge
└── RoslynSymbolGraphBuilder.cs   # Existing: unchanged; IncrementalEngine calls BuildAsync on subset

tests/DocAgent.Tests/
└── IncrementalIngestion/
    ├── FileHashManifestTests.cs      # Unit: hash/diff/serialize round-trip
    ├── IncrementalIngestionTests.cs  # Unit + integration: incremental == full, change detection
    └── IngestionMetadataTests.cs     # Unit: metadata shape, serialization round-trip
```

### Pattern 1: File Manifest Load/Diff/Save

**What:** A `FileHashManifest` record stores `IReadOnlyDictionary<string, string>` (file path → SHA-256 hex). Saved as JSON alongside the snapshot in the artifacts directory. On each run: load previous manifest → hash all current files → compute diff → identify changed/added/removed files.

**When to use:** Every incremental ingestion run. On first run, manifest is empty → treated as all files added → full ingestion (same as today).

**Example:**
```csharp
// FileHashManifest.cs
public sealed record FileHashManifest(
    IReadOnlyDictionary<string, string> FileHashes, // absolute path → SHA-256 hex
    DateTimeOffset CreatedAt,
    string SchemaVersion = "1.0");

public static class FileHasher
{
    // Stream-based to bound memory for large files
    public static async Task<string> ComputeAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

// Manifest diff result
public sealed record ManifestDiff(
    IReadOnlyList<string> AddedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> RemovedFiles)
{
    public bool HasChanges => AddedFiles.Count > 0 || ModifiedFiles.Count > 0 || RemovedFiles.Count > 0;
    public IReadOnlyList<string> ChangedFiles => [..AddedFiles, ..ModifiedFiles]; // files needing re-parse
}
```

### Pattern 2: Partial Class Resolution

**What:** Before deciding which project files to re-parse, build a "file dependency graph" from the previous snapshot's `SourceSpan` data. Group source files by the symbol IDs they contribute to. If any file in a group is changed, mark ALL files in that group for re-parsing.

**When to use:** C# partial types require this. A `partial class` spread across 2+ files — changing either file means the Roslyn compilation of the partial type needs all files.

**Example:**
```csharp
// In IncrementalIngestionEngine
private static IReadOnlySet<string> ExpandForPartialTypes(
    IReadOnlyList<string> changedFiles,
    SymbolGraphSnapshot previousSnapshot)
{
    // Build: symbolId → set of source files that contribute to it
    var symbolToFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var node in previousSnapshot.Nodes)
    {
        if (node.Span?.FilePath is { } filePath)
        {
            if (!symbolToFiles.TryGetValue(node.Id.Value, out var set))
                symbolToFiles[node.Id.Value] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(filePath);
        }
    }

    // Build: file → set of files it shares symbols with (the partial group)
    var fileGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var (_, files) in symbolToFiles)
    {
        foreach (var f in files)
        {
            if (!fileGroups.TryGetValue(f, out var group))
                fileGroups[f] = group = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            group.UnionWith(files);
        }
    }

    // Expand: for each changed file, include all files in its partial group
    var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in changedFiles)
    {
        expanded.Add(file);
        if (fileGroups.TryGetValue(file, out var group))
            expanded.UnionWith(group);
    }
    return expanded;
}
```

**NOTE:** `RoslynSymbolGraphBuilder` currently works at the project-file (.csproj) granularity, not source-file granularity. The partial class problem at source-file level is relevant if we go per-source-file. At project-file granularity, Roslyn compiles the entire project in one workspace load, so partial classes within a project are naturally handled. The simpler initial approach is per-project-file granularity (matching existing builder loop), and the partial class expansion applies to PROJECTS that contain changed source files.

### Pattern 3: Symbol Merge

**What:** After re-parsing changed project files, merge:
1. All nodes/edges from UNCHANGED projects (carried forward from previous snapshot)
2. All nodes/edges from re-parsed CHANGED projects (freshly built)
3. Re-sort via `SymbolSorter.SortNodes` / `SymbolSorter.SortEdges` to maintain determinism

**When to use:** Every incremental run that has changed files.

**Example:**
```csharp
// Merge unchanged symbols + newly parsed symbols
var preservedNodes = previousSnapshot.Nodes
    .Where(n => n.Span?.FilePath is null ||
                !affectedProjectFiles.Any(pf => IsFileInProject(n.Span.FilePath, pf)))
    .ToList();

var allNodes = new List<SymbolNode>(preservedNodes.Count + newNodes.Count);
allNodes.AddRange(preservedNodes);
allNodes.AddRange(newNodes);

return new SymbolGraphSnapshot(
    SchemaVersion: "1.0",
    ProjectName: previousSnapshot.ProjectName,
    SourceFingerprint: ComputeSourceFingerprint(currentProjectFiles),
    ContentHash: null,
    CreatedAt: DateTimeOffset.UtcNow,
    Nodes: SymbolSorter.SortNodes(allNodes),
    Edges: SymbolSorter.SortEdges(allEdges),
    IngestionMetadata: metadata);
```

### Pattern 4: IngestionMetadata Domain Type

**What:** A new Core type embedded in `SymbolGraphSnapshot`. Carries the run ID, timestamps, and file-level change log. Null on old snapshots (backward compatible).

**When to use:** Always set on new snapshots. Null-safe on read (old snapshots won't have it).

**Example:**
```csharp
// DocAgent.Core/IngestionMetadata.cs
public enum FileChangeKind { Added, Modified, Removed }

public sealed record FileChangeRecord(
    string FilePath,
    FileChangeKind ChangeKind,
    IReadOnlyList<string> AffectedSymbolIds); // SymbolId.Value strings

public sealed record IngestionMetadata(
    string RunId,               // Guid.NewGuid().ToString("N")
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool WasFullReingestion,
    IReadOnlyList<FileChangeRecord> FileChanges);
```

Then `SymbolGraphSnapshot` gains an optional property:
```csharp
// Add to existing record via with-expression pattern or new optional field
public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string ProjectName,
    string SourceFingerprint,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges,
    IngestionMetadata? IngestionMetadata = null);  // new optional field
```

### Anti-Patterns to Avoid

- **Timestamp-based change detection:** The `ComputeSourceFingerprint` in `RoslynSymbolGraphBuilder` already uses `LastWriteTimeUtc` — this is unreliable (VMs, git checkouts). Use SHA-256 content hashes.
- **Per-source-file Roslyn workspace:** Opening an `MSBuildWorkspace` per `.cs` file is not how Roslyn works. MSBuild workspace loads at project-file granularity. Stay at project-file granularity.
- **Mutating the previous snapshot:** `SymbolGraphSnapshot` is immutable. Always build a new snapshot via with-expressions or fresh construction.
- **Storing absolute paths in manifest:** Store paths relative to a stable root (the project root or artifacts dir root) to survive directory moves. Alternatively, store absolute paths but document the assumption.
- **Edge preservation without re-sorting:** Must call `SymbolSorter.SortEdges` after merge to maintain determinism invariant.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| File content hashing | Custom hash loop | `SHA256.HashDataAsync(stream, ct)` (.NET 10 BCL) | One-liner, streaming, no buffer management |
| JSON manifest persistence | Custom serializer | `System.Text.Json` with `WriteIndented = true` | Already used for SnapshotStore manifest; same pattern |
| Deterministic node/edge ordering after merge | Custom sort | `SymbolSorter.SortNodes` / `SymbolSorter.SortEdges` (existing) | Already handles the determinism contract |
| Project file discovery | Walk filesystem manually | `LocalProjectSource.DiscoverAsync` (existing) | Already handles .sln/.csproj/directory discovery |
| Snapshot storage | Custom file management | `SnapshotStore.SaveAsync` (existing) | Already handles content hash, manifest, atomic write |

**Key insight:** The heavy lifting (Roslyn parsing, sorting, serialization, storage) is already implemented. Phase 10 is a coordination layer — it decides WHAT to re-parse, then delegates to existing infrastructure.

---

## Common Pitfalls

### Pitfall 1: Project-file vs Source-file Granularity Confusion
**What goes wrong:** Trying to pass individual `.cs` files to `RoslynSymbolGraphBuilder`. It uses `MSBuildWorkspace.OpenProjectAsync(projectFile)` which requires a `.csproj` path, not source files.
**Why it happens:** The mental model of "re-parse only changed files" maps to source files, but Roslyn compiles at project granularity.
**How to avoid:** Change detection is at source-file level (hash `.cs` files), but re-parsing is at project-file level (if any source file in project X changes, re-parse project X). Map source file paths back to their containing project via the `SourceSpan.FilePath` data in the previous snapshot.
**Warning signs:** `MSBuildWorkspace.OpenProjectAsync` called with a `.cs` path throws.

### Pitfall 2: SymbolGraphSnapshot.CreatedAt Non-Determinism
**What goes wrong:** Correctness test comparing incremental vs full snapshots fails because `CreatedAt` differs.
**Why it happens:** `IngestionService` sets `CreatedAt = DateTimeOffset.UtcNow` — two independent runs get different timestamps.
**How to avoid:** In tests, normalize `CreatedAt` to a fixed value before comparing (matches existing `DeterminismTests` pattern with `FixedTimestamp`). In production, `CreatedAt` is intentionally wall-clock time and is excluded from the content hash (ContentHash is computed on `snapshot with { ContentHash = null }` in SnapshotStore).
**Warning signs:** Byte-comparison of snapshots fails only in `CreatedAt` field.

### Pitfall 3: Nodes Appearing in Multiple Project Compilations
**What goes wrong:** A shared namespace or type appears in multiple project compilations — preserving nodes from project A AND re-parsing project B that also emits a node with the same `SymbolId` produces duplicate nodes.
**Why it happens:** Roslyn compiles referenced assemblies too; global namespace symbols may appear in multiple project walks.
**How to avoid:** The existing `IsKnownNode` check in `RoslynSymbolGraphBuilder` prevents duplicate nodes within a single build. The merge must also deduplicate: use `GroupBy(n => n.Id).Select(g => g.First())` or a HashSet before sorting. Verify this is handled in tests.
**Warning signs:** `snapshot.Nodes.Count` differs between incremental and full paths.

### Pitfall 4: File Manifest Stale After Failed Run
**What goes wrong:** Manifest saved before build completes; build fails; next run skips files it shouldn't.
**Why it happens:** Saving the manifest is a side effect that must happen only after a successful snapshot is produced.
**How to avoid:** Save manifest ONLY after `SnapshotStore.SaveAsync` succeeds. Use atomic write (write to temp file, rename) same as SnapshotStore already does.
**Warning signs:** After a build failure, next incremental run produces fewer symbols than expected.

### Pitfall 5: Partial Classes Across Project Files
**What goes wrong:** `partial class Foo` defined in both `Foo.cs` (in unchanged project) and `Foo.g.cs` (generated, in changed project) — preserving old `Foo` node from unchanged project gives stale data.
**Why it happens:** Partial types compile to a single CLR type but span multiple files, potentially multiple projects (rare but happens with source generators).
**How to avoid:** At project level, if a project has ANY changed source file, re-parse the whole project — this naturally resolves partial types within a project. Cross-project partials are extremely rare; document as a known limitation for V1.
**Warning signs:** Symbols have incomplete member lists or stale doc comments.

---

## Code Examples

Verified patterns from existing codebase:

### SHA-256 File Hashing (.NET 10 BCL)
```csharp
// Streaming, no full-file buffer — good for large generated files
public static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
{
    await using var stream = new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 81920, useAsync: true);
    var hashBytes = await SHA256.HashDataAsync(stream, ct);
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
}
```

### Atomic JSON Manifest Write (matches SnapshotStore pattern)
```csharp
// Source: SnapshotStore.UpdateManifestAsync pattern
var tempPath = manifestPath + ".tmp";
var json = JsonSerializer.Serialize(manifest, JsonOptions);
await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, ct);
File.Move(tempPath, manifestPath, overwrite: true);
```

### SymbolGraphSnapshot Optional Field (C# record with default)
```csharp
// Adding optional field to existing record — backward compatible with MessagePack ContractlessStandardResolver
// because ContractlessStandardResolver uses property index order, not names.
// IMPORTANT: new fields must be appended at the END of the record to avoid breaking existing serialized data.
public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string ProjectName,
    string SourceFingerprint,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges,
    IngestionMetadata? IngestionMetadata = null);  // MUST be last
```

### Correctness Test Pattern (matches DeterminismTests)
```csharp
[Fact]
public async Task Incremental_produces_identical_snapshot_to_full_reingestion()
{
    // Arrange: ingest once to establish baseline
    var engine = CreateEngine();
    var full = await engine.IngestAsync(projectPath, forceFullReingestion: true, ct);

    // Simulate a change: modify one file
    await File.AppendAllTextAsync(changedFile, "// touch");

    // Act: incremental
    var incremental = await engine.IngestAsync(projectPath, forceFullReingestion: false, ct);

    // Normalize timestamps
    var normalizedFull = full with { CreatedAt = FixedTimestamp, ContentHash = null, IngestionMetadata = null };
    var normalizedIncremental = incremental with { CreatedAt = FixedTimestamp, ContentHash = null, IngestionMetadata = null };

    var bytesA = MessagePackSerializer.Serialize(normalizedFull, SerializerOptions);
    var bytesB = MessagePackSerializer.Serialize(normalizedIncremental, SerializerOptions);

    bytesA.SequenceEqual(bytesB).Should().BeTrue("incremental must be identical to full re-ingestion");
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Timestamp-based change detection | Content-hash (SHA-256) comparison | Decision locked in CONTEXT.md | Eliminates false negatives from clock drift and git checkout |
| Full re-ingestion every run | Incremental with file manifest | Phase 10 | Significant speedup for large codebases — only re-parse changed projects |
| `SourceFingerprint` using timestamps in `ComputeSourceFingerprint` | Will coexist (SourceFingerprint is a snapshot identity field, not change detection) | Phase 10 | SourceFingerprint remains; FileHashManifest is the change detection mechanism |

**Note:** `ComputeSourceFingerprint` in `RoslynSymbolGraphBuilder` uses `LastWriteTimeUtc` — this is the existing fingerprint field on the snapshot. The incremental engine uses a separate `FileHashManifest` with SHA-256 hashes. These serve different purposes: SourceFingerprint identifies WHAT was ingested; FileHashManifest detects WHAT changed.

---

## Open Questions

1. **Source-file-to-project mapping**
   - What we know: `SourceSpan.FilePath` contains absolute paths from Roslyn; project files end in `.csproj`
   - What's unclear: How to reliably determine which `.csproj` "owns" a given `.cs` file path (e.g., for files in shared directories)
   - Recommendation: Use directory prefix matching — a `.cs` file belongs to the nearest parent `.csproj` by directory. This is how Roslyn's workspace internally handles it. Store the file→project mapping in the FileHashManifest (alongside the hash) for O(1) lookup on subsequent runs.

2. **MessagePack ContractlessStandardResolver and optional fields**
   - What we know: ContractlessStandardResolver serializes by property order (index-based). Adding `IngestionMetadata? = null` at the END of `SymbolGraphSnapshot` is safe — existing serialized snapshots deserialize with `null` for the new field.
   - What's unclear: Whether there are existing stored snapshots in the artifacts directory that would break. Given this is a dev scaffold with no production data, this is low risk.
   - Recommendation: Add field at end, add a test round-tripping an old-format MessagePack blob to verify backward compatibility.

3. **`forceFullReingestion` integration point in IngestionService**
   - What we know: `IngestionService.IngestAsync` already has a `forceReindex` parameter for the search index.
   - What's unclear: Whether `forceFullReingestion` should be a new parameter or a boolean flag in `DocAgentServerOptions`.
   - Recommendation: Add as a parameter to `IIngestionService.IngestAsync` signature for maximum flexibility — callers (MCP tools) can pass it as needed.

---

## Validation Architecture

> `workflow.nyquist_validation` is not set in config.json — skipping this section.

---

## Sources

### Primary (HIGH confidence)
- Existing codebase: `RoslynSymbolGraphBuilder.cs` — granularity of Roslyn compilation (project-file), SHA-256 already used
- Existing codebase: `SnapshotStore.cs` — atomic write pattern, manifest.json pattern, ContentHash scheme
- Existing codebase: `DeterminismTests.cs` — correctness test pattern (byte-compare, FixedTimestamp normalization)
- Existing codebase: `IngestionServiceTests.cs` — PipelineOverride seam for testing without real Roslyn
- .NET 10 BCL docs: `SHA256.HashDataAsync(Stream, CancellationToken)` — streaming hash, no external dependency
- .NET 10 BCL docs: `System.Text.Json` — JsonSerializer already used in SnapshotStore

### Secondary (MEDIUM confidence)
- CONTEXT.md decisions — user-locked choices for hash algorithm, manifest format, metadata shape
- ROADMAP.md success criteria — "incremental snapshot must be identical to full re-ingestion"

### Tertiary (LOW confidence)
- Partial class handling approach — derived from Roslyn MSBuild workspace behavior (project-granularity compilation handles intra-project partials naturally)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies already in codebase, no new packages
- Architecture: HIGH — patterns follow established codebase conventions (SnapshotStore, DeterminismTests, PipelineOverride seam)
- Pitfalls: HIGH — derived from reading actual code paths in the codebase
- Partial class handling: MEDIUM — Roslyn project-level compilation handles it naturally, but cross-project partials are a known edge case

**Research date:** 2026-02-28
**Valid until:** 2026-03-28 (stable — .NET 10, Roslyn 4.12 APIs, no fast-moving dependencies)
