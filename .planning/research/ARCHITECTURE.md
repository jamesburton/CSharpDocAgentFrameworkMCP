# Architecture Research

**Domain:** NuGet package mapping integration into existing DocAgentFramework MCP server
**Researched:** 2026-03-26
**Confidence:** HIGH — based on direct reads of all affected source files

---

## System Overview

The existing pipeline runs: **discover → parse → normalize → index → serve → diff → review**.

v2.5 adds a lateral branch: after a solution is ingested, agents can query the NuGet dependency graph and find references to package-exported types in indexed source. This does NOT extend the main pipeline — it runs alongside it, sharing the same snapshot store and stub nodes produced during solution ingestion.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                        EXISTING PIPELINE (unchanged)                          │
│                                                                               │
│  ingest_solution ──> SolutionIngestionService ──> SymbolGraphSnapshot        │
│                            │                           │                      │
│                       stub nodes                  SnapshotStore               │
│                       (NodeKind.Stub)              BM25SearchIndex            │
│                            │                           │                      │
│                            └───────────────────────────┘                     │
└──────────────────────────────────────────────────────────────────────────────┘
                                          │
                                  (v2.5 extension)
                                          │
┌──────────────────────────────────────────────────────────────────────────────┐
│                        NEW v2.5 COMPONENTS                                    │
│                                                                               │
│  ┌─────────────────────────────────────────────────────────────────────┐     │
│  │  DocAgent.Ingestion                                                  │     │
│  │  ┌──────────────────────┐   ┌──────────────────────────────────┐   │     │
│  │  │  LockFileParser      │   │  NuGetCacheReflector             │   │     │
│  │  │                      │   │                                  │   │     │
│  │  │  reads               │   │  opens DLL via                   │   │     │
│  │  │  packages.lock.json  │   │  MetadataReference.CreateFrom... │   │     │
│  │  │  → PackageEntry[]    │   │  → SymbolNode[] (for enrichment) │   │     │
│  │  └──────────────────────┘   └──────────────────────────────────┘   │     │
│  └─────────────────────────────────────────────────────────────────────┘     │
│                    │                           │                              │
│                    ▼                           ▼                              │
│  ┌─────────────────────────────────────────────────────────────────────┐     │
│  │  DocAgent.Core                                                       │     │
│  │  PackageGraph                                                        │     │
│  │  { PackageEntry[], AssemblyMapping }                                 │     │
│  └─────────────────────────────────────────────────────────────────────┘     │
│                    │                                                          │
│                    ▼                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐     │
│  │  DocAgent.McpServer                                                  │     │
│  │                                                                       │     │
│  │  PackageQueryService        PackageTools                             │     │
│  │  (new, injectable)          [McpServerToolType]                      │     │
│  │  uses snapshot stubs  ────> get_dependencies                        │     │
│  │  + PackageGraph             find_package_usages                     │     │
│  └─────────────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## Component Responsibilities

| Component | Layer | Responsibility | New or Modified |
|-----------|-------|----------------|-----------------|
| `LockFileParser` | DocAgent.Ingestion | Parse `packages.lock.json` → `PackageEntry[]` with version, dependency type, resolved deps | **NEW** |
| `NuGetCacheReflector` | DocAgent.Ingestion | Locate DLLs in NuGet cache by package name+version, open via `MetadataReference.CreateFromFile`, walk public namespace symbols → `SymbolNode[]` | **NEW** |
| `PackageGraph` | DocAgent.Core | Immutable value type holding `PackageEntry[]` + `AssemblyMapping` (package name → DLL paths → assembly names) | **NEW** |
| `PackageEntry` | DocAgent.Core | Name, version, type (direct/transitive), framework, resolved dependency list | **NEW** |
| `StubNodeEnricher` | DocAgent.McpServer/Ingestion or DocAgent.Ingestion | Matches existing `NodeKind.Stub` nodes (by FQN/assembly name) against DLL-reflected symbols, replaces bare stubs with enriched nodes | **NEW** |
| `PackageQueryService` | DocAgent.McpServer | Loads snapshot, finds stub nodes by package assembly name, traverses edges to find source-code usages; wraps `PackageGraph` queries | **NEW** |
| `PackageTools` | DocAgent.McpServer/Tools | MCP tool handler for `get_dependencies` and `find_package_usages`; PathAllowlist enforcement | **NEW** |
| `SolutionIngestionService` | DocAgent.McpServer/Ingestion | After per-project walks, optionally trigger `LockFileParser` + `NuGetCacheReflector` + `StubNodeEnricher` per project; attach `PackageGraph` to result | **MODIFIED** (light touch) |
| `SolutionIngestionResult` | DocAgent.McpServer/Ingestion | Add optional `PackageGraphs` field (Dictionary<projectName, PackageGraph>) | **MODIFIED** (additive) |
| `ServiceCollectionExtensions` | DocAgent.McpServer | Register `PackageQueryService`, `PackageTools` | **MODIFIED** |
| `SnapshotStore` | DocAgent.Ingestion | No change — PackageGraph serialized separately | **UNCHANGED** |
| `BM25SearchIndex` | DocAgent.Indexing | No change — enriched stub nodes already filtered at index time | **UNCHANGED** |
| All existing MCP tools | DocAgent.McpServer/Tools | No change | **UNCHANGED** |

---

## Recommended Project Structure

New files go in existing projects — no new projects needed:

```
src/
├── DocAgent.Core/
│   ├── Symbols.cs              # existing — no change
│   ├── SolutionTypes.cs        # existing — no change
│   ├── PackageTypes.cs         # NEW — PackageEntry, PackageGraph, AssemblyMapping
│   └── Abstractions.cs         # existing — no change
│
├── DocAgent.Ingestion/
│   ├── LockFileParser.cs       # NEW — pure static, packages.lock.json → PackageEntry[]
│   ├── NuGetCacheReflector.cs  # NEW — Roslyn MetadataReference reflection → SymbolNode[]
│   └── (existing files unchanged)
│
├── DocAgent.McpServer/
│   ├── Ingestion/
│   │   ├── StubNodeEnricher.cs          # NEW — enriches NodeKind.Stub nodes from DLL reflection
│   │   ├── SolutionIngestionService.cs  # MODIFIED — call enrichment post-walk
│   │   └── SolutionIngestionResult.cs   # MODIFIED — add PackageGraphs field
│   └── Tools/
│       ├── PackageTools.cs              # NEW — get_dependencies, find_package_usages
│       └── (existing tool files unchanged)
│
└── DocAgent.Tests/
    ├── LockFileParserTests.cs         # NEW
    ├── NuGetCacheReflectorTests.cs    # NEW
    ├── StubNodeEnricherTests.cs       # NEW
    └── PackageToolsTests.cs           # NEW
```

### Structure Rationale

- **`PackageTypes.cs` in DocAgent.Core:** `PackageEntry` and `PackageGraph` are pure domain types with no IO. They belong in Core, where `SymbolNode`, `SymbolEdge`, and `SolutionSnapshot` live. They will be referenced by both Ingestion (producers) and McpServer (consumers).
- **`LockFileParser` and `NuGetCacheReflector` in DocAgent.Ingestion:** Lock file parsing and DLL reflection are pure data-extraction concerns — they transform external data into domain types. Ingestion already has `XmlDocParser` (XML → domain) and `RoslynSymbolGraphBuilder` (Roslyn → domain) as precedents. No dependency on McpServer.
- **`StubNodeEnricher` in DocAgent.McpServer/Ingestion:** Enrichment modifies a `SymbolGraphSnapshot` using `PackageGraph` data. It runs inside `SolutionIngestionService` which already lives in McpServer/Ingestion. Collocating keeps the modification scope clear.
- **`PackageTools` in DocAgent.McpServer/Tools:** All MCP tool handlers live here. No exception.

---

## Key Design Decisions

### PackageGraph Relation to SymbolGraphSnapshot

`PackageGraph` is **not embedded in `SymbolGraphSnapshot`**. Rationale:

1. `SymbolGraphSnapshot` is MessagePack-serialized and content-addressed. Adding a large `PackageGraph` would bloat every snapshot file and break backward compatibility with existing serialized artifacts.
2. `PackageGraph` is derived from `packages.lock.json` + DLL reflection — it is re-derivable at any time. It does not need snapshot-level versioning.
3. The relationship is: one `PackageGraph` per project, many projects per solution. The existing `SolutionIngestionResult` (not `SymbolGraphSnapshot`) is the right place to carry per-project package metadata through the ingestion pipeline.

Instead: `PackageGraph` is attached to `SolutionIngestionResult.PackageGraphs` (a `Dictionary<string, PackageGraph>` keyed by project name). `PackageTools` receives this via `PackageQueryService`, which holds the last-ingested package data in memory (or recomputes from lock files on demand).

**Storage strategy for persistence:** If agents need to query package graphs between server restarts, persist `PackageGraph` as a separate JSON sidecar file in `artifacts/` named `{snapshotHash}.packages.json`. This keeps snapshots clean and allows lazy loading. For v2.5, in-memory is sufficient — persist only if needed.

### Stub Node Enrichment Strategy

Existing stubs (`NodeKind.Stub`) have:
- `Id`: documentation comment ID from Roslyn (e.g., `T:Microsoft.Extensions.Logging.ILogger`)
- `FullyQualifiedName`: e.g., `Microsoft.Extensions.Logging.ILogger`
- `ProjectOrigin`: the assembly name (e.g., `Microsoft.Extensions.Logging.Abstractions`)
- `Docs`: null
- `Parameters`, `ReturnType`, `GenericConstraints`: empty

Enrichment goal: replace null docs and empty type info with real data reflected from the NuGet DLL.

**Enrichment matching key:** `SymbolNode.Id.Value` (the documentation comment ID). This is stable — Roslyn generates identical IDs whether from source or metadata. Match reflected `ISymbol.GetDocumentationCommentId()` against stub IDs.

**Enrichment result:** A new `SymbolNode` with the same `Id` but `NodeKind.Real` (or a new `NodeKind.Enriched` — see below), populated `Docs`, `ReturnType`, `Parameters`, `GenericConstraints`.

**NodeKind decision:** Use a new `NodeKind.Enriched = 2` value. Rationale: enriched nodes are still not from project source (Real), but they carry real metadata unlike stubs. This allows the BM25 index and query tools to handle them distinctly if needed. If `Enriched` is added, the existing `NodeKind.Real=0, NodeKind.Stub=1` ordering is preserved — append at end per MessagePack enum ordering constraint (see `feedback_messagepack_enum_ordering.md`).

**Enrichment is optional per project:** If no `packages.lock.json` exists (e.g., older project format), or if the NuGet cache does not have the DLL, the stub remains a stub. Enrichment failures are soft warnings, not hard errors.

### NuGet Cache Location Discovery

The NuGet global cache is at `%USERPROFILE%/.nuget/packages` on Windows and `~/.nuget/packages` on Linux/macOS. DLL paths follow the convention:

```
{nugetCacheRoot}/{packageId}/{version}/lib/{tfm}/{assemblyName}.dll
```

For a given `PackageEntry(Name="Microsoft.Extensions.Logging.Abstractions", Version="10.0.0")` and target framework `net10.0`, the reflector checks:

```
~/.nuget/packages/microsoft.extensions.logging.abstractions/10.0.0/lib/net10.0/*.dll
```

**TFM fallback order:** Try exact TFM first (e.g., `net10.0`), then fall back to nearest compatible TFM using a simple version comparison (e.g., `net9.0`, `net8.0`, `netstandard2.1`, `netstandard2.0`). Do not implement full NuGet compatibility graph — simple prefix matching is sufficient for reflection purposes.

**PathAllowlist consideration:** The NuGet cache is the user's local machine cache, not a user-supplied path. It does not go through the PathAllowlist (which guards tool-provided paths). The reflector uses hardcoded/config-discovered cache paths. Add a `NuGetCachePath` option to `DocAgentServerOptions` for override.

---

## Data Flow

### get_dependencies Flow

```
Agent: get_dependencies(snapshotHash, projectName)
    │
    ▼
PackageTools.GetDependencies()
    │── PathAllowlist check on snapshotStore.ArtifactsDir (existing pattern)
    │── PackageQueryService.GetPackageGraphAsync(snapshotHash, projectName)
    │       │── Load PackageGraph from in-memory cache (or artifacts/{hash}.packages.json)
    │       │── If not found: reparse lock file from project path in SolutionSnapshot
    │       └── Return PackageGraph
    │── Serialize PackageEntry[] as JSON
    └── Return to agent
```

### find_package_usages Flow

```
Agent: find_package_usages(snapshotHash, packageName)
    │
    ▼
PackageTools.FindPackageUsages()
    │── PathAllowlist check
    │── Load SymbolGraphSnapshot from SnapshotStore
    │── Filter Stub nodes where ProjectOrigin matches package assembly names
    │       (use PackageGraph.AssemblyMapping[packageName] to get assembly names)
    │── For each matching stub node:
    │       Walk snapshot.Edges where e.To == stubNode.Id and e.Scope == EdgeScope.External
    │       Collect e.From nodes (these are the source symbols referencing the package type)
    │── Return: list of (stubType, [sourceSymbolsReferencing])
    └── Serialize as JSON
```

### Stub Enrichment Flow (during ingest_solution)

```
SolutionIngestionService.IngestAsync()
    │
    │ [after per-project walk produces stubNodes list]
    │
    ▼
LockFileParser.Parse(projectDir + "/packages.lock.json")
    │── Returns PackageEntry[] (direct + transitive, with version + framework info)
    └── Soft failure: returns empty if file missing
    │
    ▼
NuGetCacheReflector.ReflectPackagesAsync(PackageEntry[], targetFramework)
    │── For each PackageEntry, find DLL in NuGet cache
    │── Open DLL via MetadataReference.CreateFromFile
    │── Walk global namespace for public symbols
    │── Returns Dictionary<assemblyName, SymbolNode[]>
    └── Soft failure: skips missing DLLs with warning
    │
    ▼
StubNodeEnricher.Enrich(stubNodes, reflectedSymbols)
    │── Match stub.Id.Value against reflected symbol IDs
    │── Replace matched stubs with enriched nodes (NodeKind.Enriched)
    └── Returns enriched SymbolNode list (replaces stubNodes in allNodes)
    │
    ▼
PackageGraph built from LockFileParser output + reflector assembly mapping
    │
    └── Stored in SolutionIngestionResult.PackageGraphs[projectName]
```

---

## Architectural Patterns to Follow

### Pattern 1: Pure Static Parsers (LockFileParser)

The existing parsers (`XmlDocParser`, `LockFileParser` in v2.3 polyglot parsers, `PowerShellScriptParser`) are pure static classes returning tuples of domain types. No DI, no IO abstractions. `LockFileParser` follows this:

```csharp
// In DocAgent.Ingestion
public static class LockFileParser
{
    public static IReadOnlyList<PackageEntry> Parse(string lockFilePath)
    { ... }

    public static IReadOnlyList<PackageEntry> ParseFromJson(string lockFileJson)
    { ... } // for testing with fixture JSON
}
```

**Why:** Zero-dependency static parsers are trivially testable with fixture files. No mocking needed. Matches the `feedback_parser_design_pattern.md` memory: "Non-C# parsers are pure static classes."

### Pattern 2: Soft Failure with Warnings

`NuGetCacheReflector` and `StubNodeEnricher` must not throw on missing DLLs or unmatched stubs. Follow the existing pattern of accumulating warnings into a `List<string> warnings` parameter:

```csharp
public static class NuGetCacheReflector
{
    public static Dictionary<string, IReadOnlyList<SymbolNode>> Reflect(
        IReadOnlyList<PackageEntry> packages,
        string targetFramework,
        string? nugetCacheRoot,
        List<string> warnings)
    { ... }
}
```

This matches `SolutionIngestionService`'s existing `warnings` list pattern.

### Pattern 3: PipelineOverride Seam for Enrichment

`SolutionIngestionService` has an existing `PipelineOverride` seam for MSBuild-free testing. The NuGet enrichment step needs a comparable seam:

```csharp
// In SolutionIngestionService
internal Func<string, List<PackageEntry>>? LockFileParserOverride { get; set; }
internal Func<IReadOnlyList<PackageEntry>, string, Dictionary<string, IReadOnlyList<SymbolNode>>>? ReflectorOverride { get; set; }
```

Tests inject fake lock file data and fake reflection results without touching the real NuGet cache or file system.

### Pattern 4: PackageQueryService as Thin Cache Layer

`PackageQueryService` does not own ingestion logic — it caches and queries results:

```csharp
public sealed class PackageQueryService
{
    private readonly SnapshotStore _snapshotStore;
    // In-memory: populated by SolutionIngestionService after each ingest
    private readonly ConcurrentDictionary<string, Dictionary<string, PackageGraph>> _cache;

    public void StorePackageGraphs(string snapshotHash, Dictionary<string, PackageGraph> graphs) { ... }
    public Dictionary<string, PackageGraph>? GetPackageGraphs(string snapshotHash) { ... }
}
```

`SolutionIngestionService` calls `StorePackageGraphs` after each successful ingest. `PackageTools` calls `GetPackageGraphs` to serve tool requests. This avoids re-running lock file parsing on every tool call.

### Pattern 5: PathAllowlist Only on Tool-Facing Paths

NuGet cache paths are internal (not tool-provided) and do not go through `PathAllowlist`. Only the snapshot store directory (already gated in SolutionTools) and any user-provided path parameters need allowlist checks. `PackageTools` follows the same `!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir)` guard used by `SolutionTools`.

---

## Integration Points

### With Existing SolutionIngestionService

`SolutionIngestionService` is the single integration point for stub enrichment. After the existing `allNodes.AddRange(stubNodes)` line, call the enrichment pipeline. The enrichment is conditional on `packages.lock.json` existing for the project directory. The signature of `IngestAsync` does not change — `PackageGraphs` flow out via `SolutionIngestionResult`.

**Exact insertion point:** After the per-project loop that builds `stubNodes` and before `SymbolSorter.SortNodes(allNodes)`. Enrichment replaces stub nodes in-place in `allNodes`.

### With Existing SnapshotStore

No change. `SnapshotStore` serializes `SymbolGraphSnapshot` objects. If enriched nodes replace stubs, the snapshot contains `NodeKind.Enriched` nodes instead — this is transparent to `SnapshotStore`. The content hash will differ from an unenriched snapshot, which is correct (different content = different hash).

### With Existing BM25SearchIndex

The existing `BM25SearchIndex.WriteDocuments` filters `n.NodeKind == NodeKind.Real`. This needs a one-line change to also include `NodeKind.Enriched` so that enriched package symbols are searchable:

```csharp
// Before:
foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
// After:
foreach (var node in snapshot.Nodes.Where(n => n.NodeKind is NodeKind.Real or NodeKind.Enriched))
```

Same one-line change in `PopulateNodes`. This is the only Indexing layer change.

### With Existing DocTools / SolutionTools

No change. These tools operate on `SymbolGraphSnapshot`. Enriched nodes are queryable via `get_symbol`, `search_symbols`, `get_references` without any tool changes.

### With Existing KnowledgeQueryService

No change. `GetReferencesAsync` traverses `EdgeScope.External` edges — these already point to stub/enriched nodes. `find_implementations` and `get_references` return enriched nodes as-is.

---

## New Dependency: Roslyn MetadataReference in DocAgent.Ingestion

`NuGetCacheReflector` needs `Microsoft.CodeAnalysis.CSharp` to open DLL metadata. This package is already in `Directory.Packages.props` at 4.14.0. The `DocAgent.Ingestion` project currently does NOT reference it (it uses `XmlDocParser` which is pure XML). Add:

```xml
<!-- In DocAgent.Ingestion/DocAgent.Ingestion.csproj -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
```

No version needed — centrally managed at 4.14.0. This is a compile-time dependency only; the Roslyn `MetadataReference.CreateFromFile` API is available in the existing pinned version.

**Alternative considered:** Put `NuGetCacheReflector` in `DocAgent.McpServer` instead, avoiding the Roslyn dep in Ingestion. Rejected because: (a) DLL reflection is a pure data-extraction concern like `RoslynSymbolGraphBuilder`, and (b) McpServer already has a heavy dependency set; isolating extraction in Ingestion keeps layer contracts clean.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Embedding PackageGraph in SymbolGraphSnapshot

**What people might do:** Add a `PackageGraph? PackageInfo` field to `SymbolGraphSnapshot`.
**Why it's wrong:** Breaks MessagePack backward compat with all existing serialized artifacts. Bloats snapshot files with data that's re-derivable from lock files. Entangles symbol graph versioning with package metadata versioning.
**Do this instead:** Keep `PackageGraph` in `SolutionIngestionResult` and `PackageQueryService`. Store as a sidecar JSON if persistence is needed.

### Anti-Pattern 2: Making Stub Enrichment Blocking on Missing DLLs

**What people might do:** Throw if a package DLL is not found in the NuGet cache.
**Why it's wrong:** The NuGet cache may be partial (not all packages restored on CI, different machine than dev). Enrichment is a quality enhancement, not a correctness requirement. A missing DLL stub is better than a failed ingestion.
**Do this instead:** Log a warning, leave the stub node as `NodeKind.Stub`, continue. Same pattern as MSBuild project failures in `SolutionIngestionService`.

### Anti-Pattern 3: Running NuGet Restore During Ingestion

**What people might do:** Shell out to `dotnet restore` to ensure packages are available before reflection.
**Why it's wrong:** Ingestion must be fast and safe. Running `dotnet restore` modifies the file system, is slow, can fail on CI, and introduces a subprocess dependency that breaks the test isolation model.
**Do this instead:** Reflect only from what is already in the NuGet cache. If the DLL is not there, skip with a warning.

### Anti-Pattern 4: New MCP Tools for Enrichment Control

**What people might do:** Add `enrich_stubs` or `reflect_packages` MCP tools.
**Why it's wrong:** Enrichment is an internal optimization pass, not a user-facing operation. Exposing it as a tool creates an ordering contract (must call after ingest) and confusing tool surface.
**Do this instead:** Run enrichment automatically inside `SolutionIngestionService` when lock files are present. Users call `ingest_solution`; enrichment happens transparently.

### Anti-Pattern 5: Per-Symbol NuGet Cache Lookup

**What people might do:** For each stub node, search the NuGet cache for matching assemblies.
**Why it's wrong:** Could be hundreds of stub nodes per project. Each cache probe is a filesystem call. This would make ingestion O(n) on stub count with filesystem overhead.
**Do this instead:** Parse the lock file first (cheap JSON parse, no filesystem probing), get the exact package list, locate each DLL once per package, reflect all symbols from that DLL in a single pass. Match stubs against the collected reflections in memory.

---

## Component Build Order

Dependencies flow downward — build in this order:

| Step | Component | File | Depends On | Notes |
|------|-----------|------|------------|-------|
| 1 | `PackageTypes.cs` | DocAgent.Core | — | Pure domain types; no deps; unblocks everything |
| 2 | `LockFileParser.cs` | DocAgent.Ingestion | Step 1 (PackageEntry) | Pure static parser; testable with fixture JSON immediately |
| 3 | `NuGetCacheReflector.cs` | DocAgent.Ingestion | Steps 1, 2; Roslyn dep added | Core complexity; critical path for enrichment |
| 4 | `StubNodeEnricher.cs` | DocAgent.McpServer/Ingestion | Steps 1, 3 | Matches stubs → enriched nodes |
| 5 | Modify `SolutionIngestionService.cs` | DocAgent.McpServer/Ingestion | Steps 2, 3, 4 | Wire enrichment pipeline; add PipelineOverride seams |
| 6 | Modify `SolutionIngestionResult.cs` | DocAgent.McpServer/Ingestion | Step 1 | Add PackageGraphs field |
| 7 | `PackageQueryService.cs` | DocAgent.McpServer | Steps 1, 5, 6 | Cache + query service; depends on ingestion result |
| 8 | Modify `BM25SearchIndex.cs` | DocAgent.Indexing | Step 1 (NodeKind.Enriched) | One-line filter change |
| 9 | `PackageTools.cs` | DocAgent.McpServer/Tools | Steps 7, 8 | MCP tools; final integration point |
| 10 | Register in `ServiceCollectionExtensions.cs` | DocAgent.McpServer | Steps 7, 9 | Wire DI |
| 11 | Tests | DocAgent.Tests | Steps 1–9 | Can start at Step 1; grow incrementally |

**Parallelism opportunities:**
- Steps 2 and 3 can develop in parallel after Step 1 (both are independent parsers/reflectors).
- Step 8 is independent of Steps 4–7 (only requires `NodeKind.Enriched` enum value from Step 1).
- Tests for each component can be written alongside that component.

**Critical path:** Step 1 → Step 3 → Step 4 → Step 5 → Step 7 → Step 9. Step 3 (`NuGetCacheReflector`) is the most complex new component and gates the enrichment half of the milestone.

---

## Modified Files Summary

| File | Change Type | Scope |
|------|-------------|-------|
| `DocAgent.Core/PackageTypes.cs` | NEW | `PackageEntry`, `PackageGraph`, `AssemblyMapping` records |
| `DocAgent.Core/Symbols.cs` | MODIFIED | Add `NodeKind.Enriched = 2` enum value |
| `DocAgent.Ingestion/DocAgent.Ingestion.csproj` | MODIFIED | Add `Microsoft.CodeAnalysis.CSharp` PackageReference |
| `DocAgent.Ingestion/LockFileParser.cs` | NEW | Pure static lock file parser |
| `DocAgent.Ingestion/NuGetCacheReflector.cs` | NEW | DLL reflection via Roslyn MetadataReference |
| `DocAgent.McpServer/Ingestion/StubNodeEnricher.cs` | NEW | Stub → enriched node matching |
| `DocAgent.McpServer/Ingestion/SolutionIngestionService.cs` | MODIFIED | Call enrichment pipeline after stub synthesis; add seams |
| `DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs` | MODIFIED | Add `PackageGraphs` field |
| `DocAgent.McpServer/PackageQueryService.cs` | NEW | In-memory package graph cache + query logic |
| `DocAgent.McpServer/Tools/PackageTools.cs` | NEW | `get_dependencies`, `find_package_usages` MCP tools |
| `DocAgent.McpServer/ServiceCollectionExtensions.cs` | MODIFIED | Register `PackageQueryService`, `PackageTools` |
| `DocAgent.Indexing/BM25SearchIndex.cs` | MODIFIED | Include `NodeKind.Enriched` in indexing filter (2 lines) |
| `DocAgent.McpServer/Config/DocAgentServerOptions.cs` | MODIFIED | Add `NuGetCachePath` config option |
| `CLAUDE.md` | MODIFIED | Document `get_dependencies`, `find_package_usages` tools |

Everything else (DocTools, ChangeTools, SolutionTools, ChangeReviewer, SnapshotStore, SymbolGraphDiffer, PathAllowlist, AuditLogger, RateLimiting, TypeScriptIngestionService, IncrementalSolutionIngestionService, all existing tests) remains **completely unchanged**.

---

## Confidence Assessment

| Area | Confidence | Basis |
|------|------------|-------|
| Integration points | HIGH | Direct reads of SolutionIngestionService, BM25SearchIndex, SolutionTools, SnapshotStore |
| PackageGraph/SymbolGraphSnapshot separation | HIGH | MessagePack backward-compat constraints verified in Symbols.cs and PROJECT.md decisions |
| Stub node enrichment mechanism | HIGH | NodeKind/EdgeScope enums read from Symbols.cs; stub synthesis in SolutionIngestionService fully traced |
| NuGet cache path convention | MEDIUM | Well-known convention, but TFM fallback ordering is implementation detail to verify |
| Roslyn MetadataReference API for DLL reflection | HIGH | Same Roslyn 4.14.0 already in use; MetadataReference.CreateFromFile is stable API |
| Build order | HIGH | Dependencies are deterministic from source reads |

---

*Architecture research for: NuGet package mapping (v2.5)*
*Researched: 2026-03-26*
