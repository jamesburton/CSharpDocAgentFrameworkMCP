# Architecture Research

**Domain:** Multi-project symbol graph integration for DocAgentFramework v1.2
**Researched:** 2026-03-01
**Confidence:** HIGH — based on direct inspection of all existing source files

---

## Existing Architecture (Baseline)

### System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                          MCP Client (Agent)                          │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ stdio
┌──────────────────────────────▼──────────────────────────────────────┐
│                         DocAgent.McpServer                           │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────────┐  │
│  │  DocTools    │  │ChangeTools   │  │    IngestionTools          │  │
│  │(search,get,  │  │(review,diff, │  │  (ingest_project)          │  │
│  │ refs, diff,  │  │ explain)     │  │                            │  │
│  │ explain)     │  │              │  │                            │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬────────────────┘  │
│         │                 │                      │                   │
│         ▼                 ▼                      ▼                   │
│  IKnowledgeQueryService  ChangeReviewer    IIngestionService         │
│  PathAllowlist (all tools)                                           │
└──────────┬───────────────────────────────────────┬──────────────────┘
           │                                       │
┌──────────▼───────────┐              ┌────────────▼──────────────────┐
│   DocAgent.Indexing  │              │      DocAgent.Ingestion        │
│  ┌─────────────────┐ │              │  ┌────────────────────────┐    │
│  │ BM25SearchIndex │ │              │  │   LocalProjectSource   │    │
│  │ (Lucene.Net)    │ │              │  │   (MSBuildWorkspace)   │    │
│  └─────────────────┘ │              │  ├────────────────────────┤    │
│  ┌─────────────────┐ │              │  │ RoslynSymbolGraphBuilder│   │
│  │KnowledgeQuery   │ │              │  │  (per-project loop)    │    │
│  │Service          │ │              │  ├────────────────────────┤    │
│  └─────────────────┘ │              │  │IncrementalIngestion    │    │
│  ┌─────────────────┐ │              │  │Engine (SHA-256 delta)  │    │
│  │  SnapshotStore  │◄├──────────────┤  ├────────────────────────┤    │
│  │  (MessagePack)  │ │              │  │    SnapshotStore        │    │
│  └─────────────────┘ │              │  └────────────────────────┘    │
└──────────────────────┘              └───────────────────────────────┘
           │
┌──────────▼───────────┐
│    DocAgent.Core     │
│  SymbolGraphSnapshot │
│  SymbolNode          │
│  SymbolEdge          │
│  IProjectSource      │
│  ISymbolGraphBuilder │
│  IKnowledgeQuerySvc  │
└──────────────────────┘
           │
┌──────────▼───────────┐
│  artifacts/ (disk)   │
│  {hash}.msgpack      │
│  manifest.json       │
│  lucene-index/       │
└──────────────────────┘
```

### Key Structural Facts from Code Inspection

**`SymbolGraphSnapshot`** (Core/Symbols.cs):
- Has a single `ProjectName: string` field — currently names the snapshot after one project or the solution file stem.
- `Nodes` and `Edges` are flat lists with no project-origin metadata on each node.
- `SourceFingerprint` is computed from project file paths only; `ContentHash` is the XxHash128 of the serialized bytes.

**`RoslynSymbolGraphBuilder`** (Ingestion):
- Already iterates multiple projects (`foreach (var projectFile in inv.ProjectFiles)`), accumulating all nodes/edges into single flat lists.
- Each project is loaded with its own `MSBuildWorkspace`, disposed after processing.
- Cross-project edges (Inherits, Implements, References) are emitted naturally because Roslyn resolves referenced symbols from metadata — but those referenced symbols are not added as `SymbolNode`s if they belong to another project or NuGet package.
- `SymbolId` is the Roslyn documentation comment ID (e.g. `T:Namespace.Type`). This is globally unique per symbol, which is the key fact enabling cross-project edge tracing.

**`LocalProjectSource`** (Ingestion):
- Already handles `.sln`, `.slnx`, `.csproj`, or directory inputs.
- For `.sln`, it uses `MSBuildWorkspace.OpenSolutionAsync` and returns all project files.
- `ProjectInventory` returns flat `ProjectFiles: IReadOnlyList<string>` — no project graph or dependency order.

**`SnapshotStore`** (Ingestion):
- Single global `manifest.json` keyed by `ContentHash`. Only stores one snapshot at a time in practice (manifest is append-only but `KnowledgeQueryService` resolves to the latest).
- No concept of per-project sub-snapshots.

**`KnowledgeQueryService`** (Indexing):
- Always resolves to latest snapshot by `IngestedAt`.
- All queries (`SearchAsync`, `GetSymbolAsync`, `GetReferencesAsync`) operate on a single flat `SymbolGraphSnapshot`.

**`BM25SearchIndex`** (Indexing):
- Indexes all nodes from a snapshot into one Lucene FSDirectory index. No project field currently stored in the Lucene document.

---

## What v1.2 Needs to Change

### The Core Problem

The existing model merges all project nodes into a single flat graph. This already works for multi-project solutions — `RoslynSymbolGraphBuilder` already loops over all projects in `ProjectInventory`. The missing capabilities are:

1. **No per-node project attribution** — cannot answer "which project owns this symbol?"
2. **No cross-project dependency edges** — when `ProjectA.Foo` calls `ProjectB.Bar`, an edge exists, but `ProjectB.Bar` has no `SymbolNode` unless `ProjectB` was also ingested.
3. **No stub/metadata nodes for NuGet types** — references to NuGet types produce dangling `References` edges.
4. **No solution-level metadata** — no concept of `ProjectReference` graph, dependency order, or which projects are test vs. production.
5. **`explain_solution` tool does not exist** — current `explain_project` describes a single project.
6. **`search_symbols` / `get_references` cannot filter by project** — no project scope parameter.

---

## Integration Points and New Components

### What to MODIFY (existing components)

#### 1. `SymbolNode` — add `ProjectOrigin` property (Core/Symbols.cs)

```csharp
// BEFORE
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    // ...
);

// AFTER
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string? ProjectOrigin,   // NEW: assembly name or project name that owns this node
    bool IsStub,             // NEW: true for NuGet/metadata-only nodes
    // ... existing fields unchanged
);
```

This is the minimal, non-breaking addition. `ProjectOrigin = null` on existing snapshots means "unknown" — backward compatible with deserialized v1.0/v1.1 snapshots via MessagePack ContractlessStandardResolver (missing fields default to null/false).

#### 2. `SymbolGraphSnapshot` — add `SolutionName` and `Projects` list (Core/Symbols.cs)

```csharp
// AFTER
public sealed record SymbolGraphSnapshot(
    string SchemaVersion,
    string ProjectName,          // Keep for backward compat; set to solution name for solution snapshots
    string? SolutionName,        // NEW: null if single-project snapshot
    IReadOnlyList<ProjectEntry> Projects,  // NEW: per-project metadata list
    string SourceFingerprint,
    string? ContentHash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges,
    IngestionMetadata? IngestionMetadata = null);

// NEW type in Core/Symbols.cs
public sealed record ProjectEntry(
    string Name,           // assembly name / project name stem
    string ProjectFilePath,
    IReadOnlyList<string> ProjectReferences,  // names of projects this one depends on
    bool IsTestProject);
```

`Projects` defaults to empty list on MessagePack deserialization — backward compatible.

#### 3. `SymbolEdge` — add `EdgeScope` (Core/Symbols.cs)

```csharp
public enum EdgeScope { IntraProject, CrossProject, CrossPackage }

public sealed record SymbolEdge(
    SymbolId From,
    SymbolId To,
    SymbolEdgeKind Kind,
    EdgeScope Scope = EdgeScope.IntraProject);   // NEW, default value = backward compatible
```

#### 4. `RoslynSymbolGraphBuilder` — tag nodes with `ProjectOrigin`, emit stub nodes (Ingestion)

The existing per-project loop already processes each project. Changes required:
- Pass `projectName` into `WalkNamespace` and `WalkType` so nodes created from that project get `ProjectOrigin` set.
- After walking a project, collect outbound `References`/`Inherits`/`Implements` edges whose `To` IDs are not in `allNodes`. For those dangling targets, emit stub `SymbolNode` entries with `IsStub = true` and `ProjectOrigin` set to the assembly name resolved from the compilation's referenced assemblies.
- Tag edges as `CrossProject` when `From.ProjectOrigin != To.ProjectOrigin`.

This is the most significant code change but is self-contained within `RoslynSymbolGraphBuilder`.

#### 5. `BM25SearchIndex` — add `project` field to Lucene document (Indexing)

Currently no project field exists in the Lucene document schema. Add a stored `project` field from `node.ProjectOrigin`. This enables `search_symbols` to accept an optional `projectFilter` parameter routed as a Lucene term query.

The `IndexAsync` method already receives the full `SymbolGraphSnapshot`. No interface change needed — just add the field to the document build in `BM25SearchIndex`.

#### 6. `IKnowledgeQueryService` + `KnowledgeQueryService` — add project filter (Indexing)

```csharp
// Add optional projectFilter to SearchAsync
Task<QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>> SearchAsync(
    string query,
    SymbolKind? kindFilter = null,
    string? projectFilter = null,   // NEW
    int offset = 0,
    int limit = 20,
    string? snapshotVersion = null,
    CancellationToken ct = default);
```

`GetReferencesAsync` already returns all edges involving a symbol. Cross-project filtering can be done client-side via `EdgeScope`, or a `crossProjectOnly` parameter added.

#### 7. `DocTools` — add `projectFilter` param to `search_symbols`, add `crossProjectOnly` to `get_references` (McpServer)

- `search_symbols`: add optional `projectFilter` string parameter, pass to `IKnowledgeQueryService`.
- `get_references`: add optional `crossProjectOnly` bool parameter.

#### 8. `SnapshotManifestEntry` — add `SolutionName` and `ProjectCount` (Ingestion)

Currently has `ProjectName` (single string). Extend with:
```csharp
public sealed record SnapshotManifestEntry(
    string ContentHash,
    string ProjectName,
    string? SolutionName,   // NEW
    int ProjectCount,       // NEW
    // ... existing fields
);
```

---

### What to ADD (new components)

#### NEW: `SolutionDependencyResolver` (DocAgent.Ingestion)

**Responsibility:** Given a list of project metadata from `MSBuildWorkspace.OpenSolutionAsync`, compute the `ProjectReference` dependency graph and topological order.

**Why needed:** `RoslynSymbolGraphBuilder` currently processes projects in the order returned by `LocalProjectSource`. For correct stub node promotion (a stub created for `ProjectA.Bar` when processing `ProjectC.Foo` should be replaced by the real node when `ProjectA` is later processed), projects must be processed in dependency order — leaf projects first, consumers last.

```csharp
// DocAgent.Ingestion/SolutionDependencyResolver.cs
public static class SolutionDependencyResolver
{
    // Returns project file paths in dependency order (leaves first).
    public static IReadOnlyList<string> TopologicalSort(
        IReadOnlyList<(string FilePath, IReadOnlyList<string> ReferencedProjectPaths)> projects);

    // Builds ProjectEntry list for snapshot metadata.
    public static IReadOnlyList<ProjectEntry> BuildProjectEntries(
        IReadOnlyList<(string FilePath, IReadOnlyList<string> ReferencedProjectPaths)> projects,
        Func<string, bool> isTestProject);
}
```

This is a static pure-logic service (same pattern as `ChangeReviewer`, `SymbolGraphDiffer`).

#### NEW: `SolutionGraphMerger` (DocAgent.Ingestion)

**Responsibility:** After `RoslynSymbolGraphBuilder` builds a graph with stub nodes, the merger promotes stub nodes that were resolved by later-processed projects. It deduplicates nodes (same `SymbolId` appearing as both real and stub) and marks remaining stubs.

```csharp
// DocAgent.Ingestion/SolutionGraphMerger.cs
public static class SolutionGraphMerger
{
    // Replaces stub nodes with real nodes where a real node with matching SymbolId exists.
    // Updates edge Scope flags for cross-project edges.
    // Returns merged node + edge lists.
    public static (IReadOnlyList<SymbolNode> Nodes, IReadOnlyList<SymbolEdge> Edges)
        Merge(IReadOnlyList<SymbolNode> nodes, IReadOnlyList<SymbolEdge> edges);
}
```

Also a static pure-logic service, easily tested with fixtures.

#### NEW: `SolutionTools` (DocAgent.McpServer/Tools)

**Responsibility:** Implement `explain_solution` MCP tool.

```
explain_solution: Returns solution-level architecture overview including:
- List of projects with node/edge counts per project
- ProjectReference dependency graph as adjacency list
- Top cross-project dependency pairs (most referenced project pairs)
- NuGet stub node count
- Snapshot freshness indicator
```

This is a new `[McpServerToolType]` class following the exact same pattern as `DocTools` and `ChangeTools`. It depends on `IKnowledgeQueryService` (reads the snapshot's `Projects` list and does cross-project edge analysis) and `PathAllowlist`.

---

## Data Flow Changes

### Solution Ingestion Flow (v1.2)

```
ingest_project (path = solution.sln)
    |
    v
LocalProjectSource.DiscoverAsync
    ├── MSBuildWorkspace.OpenSolutionAsync
    ├── Extract project files + ProjectReference graph   [MODIFIED]
    └── Returns ProjectInventory
         + new: ProjectDependencies adjacency map        [NEW field]
    |
    v
SolutionDependencyResolver.TopologicalSort              [NEW]
    └── Reorders ProjectInventory.ProjectFiles by dependency
    |
    v
RoslynSymbolGraphBuilder.BuildAsync                     [MODIFIED]
    ├── Per-project loop (already exists)
    ├── Tag each node with ProjectOrigin                 [NEW]
    ├── After each project: emit stub nodes for          [NEW]
    |   dangling edge targets from other assemblies
    └── Accumulate all nodes + edges (as before)
    |
    v
SolutionGraphMerger.Merge                               [NEW]
    ├── Promote stubs to real nodes where resolved
    ├── Mark remaining stubs (IsStub = true)
    └── Tag cross-project edges (EdgeScope)
    |
    v
SymbolGraphSnapshot with:
    ├── SolutionName set                                 [NEW field]
    ├── Projects list with ProjectEntry records          [NEW field]
    ├── Nodes with ProjectOrigin + IsStub                [NEW fields]
    └── Edges with EdgeScope                             [NEW field]
    |
    v
SnapshotStore.SaveAsync (unchanged)
    |
    v
BM25SearchIndex.IndexAsync                              [MODIFIED]
    └── Adds "project" field to Lucene document
```

### Cross-Project Reference Query Flow (v1.2)

```
get_references(symbolId, crossProjectOnly = true)
    |
    v
IKnowledgeQueryService.GetReferencesAsync              [MODIFIED]
    └── Filters edges where EdgeScope == CrossProject
    |
    v
Returns cross-project SymbolEdge list
    (From.ProjectOrigin and To.ProjectOrigin differ)
```

---

## Component Boundaries: New vs. Modified

| Component | File | Status | Change Summary |
|-----------|------|--------|----------------|
| `SymbolNode` | Core/Symbols.cs | MODIFY | Add `ProjectOrigin?`, `IsStub` |
| `SymbolEdge` | Core/Symbols.cs | MODIFY | Add `EdgeScope` with default |
| `SymbolGraphSnapshot` | Core/Symbols.cs | MODIFY | Add `SolutionName?`, `Projects` list |
| `ProjectEntry` | Core/Symbols.cs | ADD | New record for project metadata |
| `EdgeScope` | Core/Symbols.cs | ADD | New enum |
| `IKnowledgeQueryService` | Core/Abstractions.cs | MODIFY | Add `projectFilter` to `SearchAsync` |
| `ProjectInventory` | Core/Abstractions.cs | MODIFY | Add `ProjectDependencies` adjacency |
| `LocalProjectSource` | Ingestion | MODIFY | Populate `ProjectDependencies` from Roslyn solution |
| `RoslynSymbolGraphBuilder` | Ingestion | MODIFY | Tag nodes, emit stubs, tag edge scopes |
| `SolutionDependencyResolver` | Ingestion | ADD | Topological sort, ProjectEntry builder |
| `SolutionGraphMerger` | Ingestion | ADD | Stub promotion, cross-project edge tagging |
| `SnapshotManifestEntry` | Ingestion/SnapshotStore.cs | MODIFY | Add `SolutionName`, `ProjectCount` |
| `BM25SearchIndex` | Indexing | MODIFY | Add `project` Lucene field |
| `KnowledgeQueryService` | Indexing | MODIFY | Thread `projectFilter` through `SearchAsync` |
| `DocTools` | McpServer/Tools | MODIFY | Add `projectFilter` to `search_symbols`, `crossProjectOnly` to `get_references` |
| `SolutionTools` | McpServer/Tools | ADD | `explain_solution` tool |
| `IngestionTools` | McpServer/Tools | NO CHANGE | `ingest_project` already accepts `.sln` path |

---

## Architectural Patterns to Follow

### Pattern 1: Static Pure-Logic Service (established in v1.1)

`ChangeReviewer` and `SymbolGraphDiffer` are static classes with no DI dependencies. Apply the same pattern to `SolutionDependencyResolver` and `SolutionGraphMerger`. This makes them trivial to unit-test with in-memory fixtures, no mock setup required.

### Pattern 2: Backward-Compatible Domain Model Extension

`DiffTypes.cs` in v1.1 used nullable optional detail fields on `DiffEntry` to avoid polymorphic MessagePack serialization. For v1.2, the same principle applies: new fields on `SymbolNode`, `SymbolEdge`, and `SymbolGraphSnapshot` should have null/default values so existing MessagePack artifacts deserialize without error via `ContractlessStandardResolver`.

Verify: `SchemaVersion` is currently `"1.0"` for all snapshots. After v1.2 changes, bump to `"1.2"` in `RoslynSymbolGraphBuilder.BuildAsync`. `SnapshotStore` and `KnowledgeQueryService` should tolerate both versions.

### Pattern 3: Opaque Denial in Security Gate

`PathAllowlist` enforcement is already applied uniformly across all tool classes. `SolutionTools` must follow the same pattern — check `PathAllowlist` before any graph access. For `explain_solution`, the "path" being checked is the snapshot's root path, which is stored in `IngestionMetadata`.

### Pattern 4: Per-Path Semaphore in `IngestionService`

The existing per-path semaphore in `IngestionService` already prevents concurrent ingestion of the same solution. No change needed here for v1.2. The semaphore key is `Path.GetFullPath(path)`, which works equally well for `.sln` paths.

---

## Build Order for Implementation

Dependencies flow upward — implement bottom-up:

```
Phase 1: Core model extensions
    ├── SymbolNode.ProjectOrigin, SymbolNode.IsStub
    ├── SymbolEdge.EdgeScope enum + field
    ├── SymbolGraphSnapshot.SolutionName, .Projects
    ├── ProjectEntry record
    ├── IKnowledgeQueryService.SearchAsync projectFilter param
    └── ProjectInventory.ProjectDependencies

Phase 2: Ingestion layer
    ├── SolutionDependencyResolver (NEW, pure static)
    ├── LocalProjectSource — populate ProjectDependencies
    ├── RoslynSymbolGraphBuilder — tag ProjectOrigin, emit stubs
    └── SolutionGraphMerger (NEW, pure static)

Phase 3: SnapshotStore + manifest
    └── SnapshotManifestEntry — add SolutionName, ProjectCount

Phase 4: Indexing layer
    ├── BM25SearchIndex — add project Lucene field
    └── KnowledgeQueryService — thread projectFilter

Phase 5: MCP tool layer
    ├── DocTools — add projectFilter, crossProjectOnly params
    └── SolutionTools (NEW) — explain_solution tool
```

Each phase is independently testable. Phases 2 and 4 are the longest; phases 1 and 3 are the safest starting points since they only add fields.

---

## Scaling Considerations

| Concern | At 10 projects | At 100 projects | Notes |
|---------|---------------|-----------------|-------|
| Memory during ingestion | Fine | May spike during build | Existing MSBuildWorkspace-per-project + dispose pattern handles this |
| Stub node count | Low (hundreds) | Can grow to tens of thousands for NuGet types | Cap stub nodes to direct assembly references only, not transitive |
| Lucene index size | Increases proportionally | Linear growth | Acceptable; no architectural change needed |
| Topological sort | Trivial | Trivial (Kahn's algorithm, O(V+E)) | `SolutionDependencyResolver` is never a bottleneck |
| `explain_solution` response size | Small | Medium (100+ projects) | Paginate project list if needed; out of scope for v1.2 |

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Per-Project Snapshots

**What people do:** Store one `SymbolGraphSnapshot` per project, then merge at query time.

**Why it's wrong:** Breaks the existing single-snapshot model that `KnowledgeQueryService`, `BM25SearchIndex`, and `SnapshotStore` all depend on. Requires a query-time merge layer, complicates the diff model, and invalidates the content-hash scheme. The existing architecture already handles multi-project within one snapshot — `RoslynSymbolGraphBuilder` already loops over all projects.

**Do this instead:** Extend the single snapshot model with `ProjectOrigin` on nodes and a `Projects` list on the snapshot. Keep one active snapshot per solution.

### Anti-Pattern 2: Blocking on Roslyn MetadataReference for NuGet Stubs

**What people do:** For every dangling `References` edge, open the referenced NuGet DLL via Roslyn to extract symbol information.

**Why it's wrong:** NuGet DLLs are not on the PathAllowlist, may not be present on all machines, dramatically increases ingestion time, and was explicitly deferred to V1.5.

**Do this instead:** Emit lightweight stub nodes with `IsStub = true`, `ProjectOrigin` set to the assembly name, and minimal fields (just `Id`, `DisplayName`, `Kind = Type`). Stubs satisfy edge validity without full symbol data.

### Anti-Pattern 3: Changing `SymbolId` Format for Cross-Project Disambiguation

**What people do:** Prefix symbol IDs with project name (e.g. `ProjectA::T:Foo.Bar`) to make cross-project IDs unique.

**Why it's wrong:** Roslyn documentation comment IDs (the existing `SymbolId` basis) are already globally unique across a solution — the fully-qualified type name is unambiguous. Changing the ID format breaks all existing snapshots, tests, and client tooling.

**Do this instead:** Keep `SymbolId` format unchanged. Use `ProjectOrigin` as an attribute on the node, not part of the identifier.

### Anti-Pattern 4: Solution-Level IngestionService Redesign

**What people do:** Create a separate `SolutionIngestionService` to handle multi-project as a distinct code path.

**Why it's wrong:** `IngestionService` already routes `.sln` paths through `LocalProjectSource.DiscoverFromSolutionAsync`, producing a `ProjectInventory` with all project files. The pipeline difference for v1.2 is inside `RoslynSymbolGraphBuilder` and the new `SolutionGraphMerger` — not in `IngestionService`.

**Do this instead:** Add `SolutionDependencyResolver` and `SolutionGraphMerger` calls within the existing `IngestionService.IngestAsync` pipeline, between discovery and building.

---

## Integration Points Summary

| Boundary | Communication | Change Required |
|----------|---------------|-----------------|
| Core to Ingestion | `ProjectInventory`, `SymbolGraphSnapshot` records | Add fields to both |
| Core to Indexing | `IKnowledgeQueryService` interface | Add `projectFilter` param |
| Core to McpServer | `IKnowledgeQueryService`, `SymbolGraphSnapshot` | Add `projectFilter` param, read new snapshot fields |
| Ingestion internal | `LocalProjectSource` to `RoslynSymbolGraphBuilder` | `LocalProjectSource` populates dep graph; builder uses it |
| Ingestion to Indexing | `SymbolGraphSnapshot` passed to `BM25SearchIndex.IndexAsync` | Index reads new `ProjectOrigin` field from nodes |
| McpServer to Ingestion | `IngestionService.IngestAsync` | Insert `SolutionDependencyResolver` + `SolutionGraphMerger` calls |
| McpServer tool surface | New `explain_solution` tool | New `SolutionTools` class |

---

## Sources

- Direct inspection: `DocAgent.Core/Symbols.cs`, `DocAgent.Core/Abstractions.cs`
- Direct inspection: `DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs`, `LocalProjectSource.cs`, `SnapshotStore.cs`
- Direct inspection: `DocAgent.Indexing/KnowledgeQueryService.cs`, `BM25SearchIndex.cs`
- Direct inspection: `DocAgent.McpServer/Tools/DocTools.cs`, `IngestionTools.cs`, `ChangeTools.cs`
- Direct inspection: `DocAgent.McpServer/Ingestion/IngestionService.cs`
- Project context: `.planning/PROJECT.md`
- Roslyn `Project.ProjectReferences` and `Solution.Projects` APIs for dependency graph extraction

---

*Architecture research for: DocAgentFramework v1.2 Multi-Project Symbol Graphs*
*Researched: 2026-03-01*
