# Feature Research

**Domain:** Multi-project / solution-level symbol graph — DocAgentFramework v1.2 milestone
**Researched:** 2026-03-01
**Confidence:** HIGH (domain well-understood from codebase + Roslyn ecosystem; MSBuildWorkspace quirks verified via official sources)

> **MILESTONE SCOPE NOTE:** v1.0 and v1.1 features are already built. This file covers only what is NEW for v1.2.
> Already built: single-project ingestion, BM25 search, 8 MCP tools, semantic diff, incremental ingestion, PathAllowlist security.

---

## Context: What Exists (v1.1 Baseline)

The following are in production and must NOT be regressed:

- `SymbolGraphSnapshot` — single-project, versioned, deterministic, MessagePack-serialized
- `RoslynSymbolGraphBuilder` — walks Roslyn symbols for one `ProjectInventory`
- `IncrementalIngestionEngine` — SHA-256 file hashing, only changed files re-parsed
- `BM25SearchIndex` (Lucene.Net) — keyword search over one snapshot
- `SnapshotStore` — content-addressed artifact store with `manifest.json`
- 8 MCP tools: `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`, `review_changes`, `find_breaking_changes`, `explain_change`
- `PathAllowlist` security enforcement on all tool classes

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features an agent or developer will assume exist when "solution-level graphs" are claimed. Missing these makes the feature feel incomplete or broken.

| Feature | Why Expected | Complexity | Dependency on Existing |
|---------|--------------|------------|------------------------|
| Ingest an entire `.sln` file in one call (`ingest_solution` tool) | "Solution-level" implies single-shot ingestion; project-by-project is not acceptable | MEDIUM | Extends `IProjectSource`; MSBuildWorkspace `OpenSolutionAsync` handles multi-project compilation setup |
| `SolutionSnapshot` aggregate — unified graph spanning all projects | Agents search across the whole solution, not per project; a collection of disconnected snapshots is not a "solution graph" | HIGH | `SymbolGraphSnapshot.ProjectName` is singular; a new aggregate record is required rather than extending the single-project type |
| `ProjectReference` cross-project `SymbolEdge`s | Without cross-project edges, "inherits", "implements", "calls" stop at project boundaries; the graph is broken for multi-project solutions | HIGH | `SymbolEdge` and `SymbolEdgeKind` already defined; builder needs cross-compilation symbol resolution |
| `search_symbols` returns results from ALL projects | Agents search by type name and expect solution-wide hits, not per-project isolation | MEDIUM | `BM25SearchIndex.IndexAsync` must accept a merged or solution-level snapshot |
| `get_references` spans project boundaries | "Who calls this?" is only useful if it crosses project boundaries | MEDIUM | `GetReferencesAsync` currently walks edges in one snapshot; must walk the solution-level edge set |
| `get_symbol` resolves by fully qualified name across any project | Agents use FQN lookups; the project of origin is usually unknown | LOW | `ISearchIndex.GetAsync(SymbolId)` already works; needs unified lookup scope |
| Skip non-C# projects gracefully | F# and VB projects appear in `.sln` files; the tool must not crash | LOW | Detect via MSBuildWorkspace project language; emit a warning node, continue |

### Differentiators (Competitive Advantage)

Features beyond the minimum that increase agent utility and distinguish this system from basic per-project ingestion.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Stub/metadata nodes for NuGet package types | Agents can follow edges to external types (BCL, NuGet) without full source ingestion; stubs show type names, namespaces, member signatures — enough to answer "what is this type?" | MEDIUM | Stub nodes flagged `IsExternal = true`; no doc comment, no `SourceSpan`; generated from Roslyn `MetadataReference` symbols on any project |
| `explain_solution` MCP tool | Solution-level architecture overview: project list, dependency DAG, node/edge counts per project, doc coverage per project; gives agents a map before they dig in | MEDIUM | New tool in `DocTools`; aggregates `SolutionSnapshot` data; no new indexing required |
| Project dependency DAG as first-class data | Agents can ask "which projects depend on DocAgent.Core?" at the project level — a different question from symbol-level edges | LOW | Add `ProjectEdge[]` collection to `SolutionSnapshot`; separate from `SymbolEdge`; directly readable from MSBuildWorkspace's `ProjectReference` graph |
| Per-project incremental re-ingestion within a solution | Change one project → re-ingest only that project, merge back into solution graph; avoids full solution re-parse on every edit | HIGH | Extends `IncrementalIngestionEngine`; requires a manifest-of-manifests keyed by project path; most complex feature in this milestone |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Full NuGet package source ingestion | "I want to see BCL implementations" | Roslyn + BCL + transitive NuGet = millions of symbols; index bloats to gigabytes; ingestion takes minutes on any real solution | Stub/metadata nodes only: type name, namespace, member signatures with no doc comments or source spans |
| One giant merged `SymbolGraphSnapshot` replacing all project snapshots | Simpler query model | `SymbolGraphSnapshot.ProjectName` is singular; flattening loses per-project identity; breaks determinism contract (order-dependence); makes incremental ingestion stateless | Aggregate `SolutionSnapshot` referencing per-project snapshots by stable ID; merge only the search index layer |
| Real-time file-watch re-ingestion | "Always up to date" | `FileSystemWatcher` on large solutions produces event storms; complicates consistency guarantees; MCP server model is request-driven, not event-driven | Explicit `ingest_solution` trigger tool; agents call it when they know code has changed |
| Cross-language graph (C# + F# in same solution) | F# projects appear in real `.sln` files | F# Roslyn support is incomplete; polyglot is explicitly deferred to a future tier in the project plan | Skip non-C# projects with a logged warning; include a `SkippedProject` list in `SolutionSnapshot` metadata |
| Full interprocedural call graph across all assemblies | "Show me every call chain to this method" | Whole-program call graph analysis is non-trivial with Roslyn; `FindReferencesAsync` is expensive at scale; this is a multi-minute operation on large solutions | Cross-project `References` edges at call-site level, scoped to solution projects only; not all-assemblies |

---

## Feature Dependencies

```
[.sln parsing / MSBuildWorkspace integration]
    └──requires──> [Multi-project ProjectInventory (update to IProjectSource)]

[SolutionSnapshot aggregate]
    └──requires──> [.sln parsing / MSBuildWorkspace integration]
    └──requires──> [per-project SymbolGraphSnapshot (already built)]

[Cross-project SymbolEdges]
    └──requires──> [SolutionSnapshot aggregate]
    └──requires──> [Roslyn cross-compilation reference resolution in builder]

[Unified BM25 search index (solution-wide)]
    └──requires──> [SolutionSnapshot aggregate]
    └──enhances──> [BM25SearchIndex (already built — reuse with merged node set)]

[search_symbols / get_symbol / get_references — solution-aware]
    └──requires──> [Unified BM25 search index (solution-wide)]
    └──requires──> [Cross-project SymbolEdges]

[explain_solution MCP tool]
    └──requires──> [SolutionSnapshot aggregate]
    └──enhances──> [Project dependency DAG]

[ingest_solution MCP tool]
    └──requires──> [.sln parsing / MSBuildWorkspace integration]

[Stub/metadata nodes for NuGet]
    └──requires──> [Multi-project ProjectInventory]
    └──enhances──> [Cross-project SymbolEdges (fills gaps at project boundaries)]

[Per-project incremental re-ingestion within solution]
    └──requires──> [SolutionSnapshot aggregate]
    └──requires──> [IncrementalIngestionEngine (already built)]
    └──requires──> [manifest-of-manifests keyed by project path]

[Project dependency DAG]
    └──requires──> [.sln parsing / MSBuildWorkspace integration]
    └──independent of SymbolEdges (project-level, not symbol-level)
```

### Dependency Notes

- **`SolutionSnapshot` must NOT replace `SymbolGraphSnapshot`.** The single-project type stays as the per-project artifact; `SolutionSnapshot` is a new aggregate. This preserves backward compatibility with all existing tools and tests.
- **Cross-project edges require multi-project compilation.** MSBuildWorkspace `OpenSolutionAsync` builds all compilations with inter-project references already resolved. The builder must receive a mapping of `IAssemblySymbol → project name` to emit cross-project `SymbolEdge`s correctly.
- **Unified search depends on aggregate, not on cross-project edges.** The search index can be merged (all node sets concatenated) without cross-project edges. Edges enhance navigation but are not required for search. This means search-aware can ship slightly ahead of full edge resolution.
- **Incremental per-project re-ingestion is the most complex feature** and has the most dependencies. It should be implemented last in the milestone, after the basic solution graph is working.
- **PathAllowlist enforcement extends naturally.** `ingest_solution` and `explain_solution` must go through the existing `PathAllowlist` check like all other tools. The `.sln` file path must be within the allowlist.

---

## MVP Definition

### Launch With (v1.2 core)

Minimum to deliver on "solution-level graphs":

- [ ] **`.sln` ingestion via MSBuildWorkspace** — `OpenSolutionAsync`, enumerate C# projects, skip non-C# with warning; produces ordered list of project compilations
- [ ] **`SolutionSnapshot` aggregate type** — new record: `IReadOnlyList<SymbolGraphSnapshot> Projects`, `IReadOnlyList<ProjectEdge> ProjectDependencies`, solution name, schema version
- [ ] **Cross-project `SymbolEdge`s** — emit Inherits/Implements/References edges across project boundaries; requires builder access to project-to-assembly map
- [ ] **Unified BM25 search index** — merge all project node sets; preserve `ProjectName` on each node for optional filtering
- [ ] **Existing tools become solution-aware** — `search_symbols`, `get_symbol`, `get_references` operate on unified index; no tool signature changes
- [ ] **`explain_solution` MCP tool** — solution overview: project list, dependency DAG, per-project node/edge counts, doc coverage per project
- [ ] **`ingest_solution` MCP tool** — explicit trigger; full re-ingestion; PathAllowlist-enforced

### Add After Core Validates (v1.2 follow-on)

- [ ] **Stub/metadata nodes for NuGet packages** — add after cross-project edges are working; enriches edge completeness at solution boundaries
- [ ] **Per-project incremental re-ingestion within solution** — re-ingest only changed projects; manifest-of-manifests; most complex; add after core solution graph is stable and tested

### Future Consideration (v2+)

- [ ] **Vector/semantic search over solution graph** — `IVectorIndex` interface already stubbed; embeddings provider TBD
- [ ] **Package reference graph** — explicitly deferred to v1.5 per PROJECT.md
- [ ] **HTTP/SSE MCP transport** — deferred; auth model not yet designed
- [ ] **Polyglot support** — deferred to future tier; generic `ISymbolGraphBuilder` contract needed first

---

## Feature Prioritization Matrix

| Feature | Agent Value | Implementation Cost | Priority |
|---------|-------------|---------------------|----------|
| `.sln` ingestion via MSBuildWorkspace | HIGH | MEDIUM | P1 |
| `SolutionSnapshot` aggregate type | HIGH | MEDIUM | P1 |
| Cross-project `SymbolEdge`s | HIGH | HIGH | P1 |
| Unified BM25 search (solution-wide) | HIGH | MEDIUM | P1 |
| Existing tools become solution-aware | HIGH | LOW | P1 |
| `explain_solution` MCP tool | MEDIUM | LOW | P1 |
| `ingest_solution` MCP tool | MEDIUM | LOW | P1 |
| Stub/metadata nodes for NuGet | MEDIUM | MEDIUM | P2 |
| Per-project incremental re-ingestion | MEDIUM | HIGH | P2 |
| Project dependency DAG first-class | LOW | LOW | P2 |

**Priority key:**
- P1: Required to deliver v1.2 milestone goal
- P2: Valuable follow-on; add within milestone if capacity allows
- P3: Future milestone

---

## Known Complexity and Risk Notes

### MSBuildWorkspace Integration Risk

MSBuildWorkspace requires MSBuild installed on the host machine. Documented issues (verified via GitHub issues):

- `ProjectReference` resolution can produce empty `MetadataReferences` if build artifacts are stale — projects must be built before `OpenSolutionAsync` or the compilation will be semantically incomplete
- Duplicate project loading when one project appears under multiple solution folders in the `.sln` file
- Non-C# projects (F#, VB) load into the workspace but `GetCompilationAsync()` for them may behave differently — skip explicitly by checking `project.Language`
- `workspace.Diagnostics` (after `OpenSolutionAsync`) must be checked and logged; silent failures are common

**Mitigation:** Wrap `OpenSolutionAsync` with diagnostic capture; emit all workspace diagnostics as structured telemetry; treat any project with null or empty compilation as skipped.

### `SolutionSnapshot` Design Decision

`SymbolGraphSnapshot.ProjectName` is a scalar — it is a single-project contract. Two design options:

1. **New `SolutionSnapshot` record (recommended):** `IReadOnlyList<SymbolGraphSnapshot> Projects` + `IReadOnlyList<ProjectEdge> ProjectDependencies` + solution-level metadata. Clean separation. `SymbolGraphSnapshot` is unchanged. `SnapshotStore` and `IKnowledgeQueryService` need solution-level variants.
2. **Extend `SymbolGraphSnapshot` with optional `SolutionName` + `ProjectSnapshots` list:** Backwards-compatible at the type level but conceptually pollutes a single-project type with solution concerns.

Option 1 is the correct choice given the existing clean layering. `SymbolGraphSnapshot` stays a single-project artifact.

### Cross-Project Edge Resolution

Roslyn resolves `ProjectReference` as `IAssemblySymbol` in referenced compilations automatically when using `OpenSolutionAsync`. To emit cross-project `SymbolEdge`s:

1. After `OpenSolutionAsync`, build a `Dictionary<IAssemblySymbol, string>` mapping each assembly to its project name
2. During symbol graph building, when `INamedTypeSymbol.BaseType` or `.Interfaces` resolves to a symbol in a different assembly (detected via the dictionary), emit a cross-project edge using the target project's FQN-based `SymbolId`
3. The builder must receive this cross-project context as a new parameter or a new `ISolutionContext` interface

This is a clean extension of the existing builder pattern but requires a new builder interface variant or overload.

---

## Sources

- MSBuildWorkspace documented behavior and quirks: [Using MSBuildWorkspace (Dustin Campbell, Microsoft)](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3)
- MSBuildWorkspace cross-project reference issues: [dotnet/roslyn #36072](https://github.com/dotnet/roslyn/issues/36072), [dotnet/roslyn #25921](https://github.com/dotnet/roslyn/issues/25921)
- Solution analysis patterns: [Steve Gordon — Using Roslyn APIs to Analyse a .NET Solution](https://www.stevejgordon.co.uk/using-the-roslyn-apis-to-analyse-a-dotnet-solution)
- Existing codebase reviewed: `DocAgent.Core/Symbols.cs`, `DocAgent.Core/Abstractions.cs`, `DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs`, `DocAgent.McpServer/Tools/`
- PROJECT.md v1.2 milestone definition

---

*Feature research for: DocAgentFramework v1.2 multi-project / solution-level graphs*
*Researched: 2026-03-01*
