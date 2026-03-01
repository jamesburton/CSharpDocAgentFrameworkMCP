# Project Research Summary

**Project:** DocAgentFramework v1.2 — Multi-Project / Solution-Level Symbol Graphs
**Domain:** .NET Roslyn-based code documentation ingestion with MCP server
**Researched:** 2026-03-01 (v1.2 research synthesized over v1.0 baseline from 2026-02-26)
**Confidence:** HIGH

## Executive Summary

DocAgentFramework v1.2 extends an already-working single-project symbol graph pipeline to operate at the solution level. The project has a strong foundation: v1.1 ships `SymbolGraphSnapshot`, `RoslynSymbolGraphBuilder`, `BM25SearchIndex`, 8 MCP tools, incremental ingestion, and `PathAllowlist` security enforcement. The v1.2 milestone does not require architectural rethinking — it requires targeted, backward-compatible extensions to an established pipeline. The recommended approach is to treat the existing flat multi-node graph as the preserved contract, extend it with per-node attribution (`ProjectOrigin`, `IsStub`), add two new static pure-logic services (`SolutionDependencyResolver`, `SolutionGraphMerger`), and surface solution-level data through one new MCP tool (`explain_solution`) and one new trigger tool (`ingest_solution`). The single-snapshot model is preserved; per-project sub-snapshots are explicitly rejected as an anti-pattern.

The critical technical dependency is `Microsoft.Build.Locator` 1.11.2, which must be explicitly referenced in `DocAgent.McpServer` and `DocAgent.AppHost` and called as `MSBuildLocator.RegisterDefaults()` before any MSBuild type is loaded. The existing `LocalProjectSource` already calls `MSBuildWorkspace.OpenSolutionAsync` for `.sln` inputs, so the workspace integration is partially in place — what is missing is dependency ordering, per-node project attribution, stub node generation, and merged indexing. Three net-new NuGet packages cover all gaps: `Microsoft.Build.Locator` 1.11.2, `NuGet.Packaging` 6.12.0, and `NuGet.Configuration` 6.12.0.

The main risks are MSBuildWorkspace memory growth (every snapshot build loads full compilations into memory — these must be extracted to plain data and released immediately), STDIO contamination corrupting the MCP stream (all logging must go to stderr — established from v1.1 but worth re-verifying), and non-deterministic snapshot serialization (all collections must be sorted before serialization). All three are known patterns with clear mitigations. The implementation is a bottom-up, phase-by-phase build: Core model extensions first, then Ingestion layer, then Indexing, then MCP tools — each phase independently testable.

---

## Key Findings

### Recommended Stack

The v1.2 stack adds exactly three packages on top of the already-pinned v1.1 dependencies. `Microsoft.Build.Locator` 1.11.2 is the required companion to `MSBuildWorkspace` for standalone host processes — without it, workspace construction throws a MEF composition exception with no useful diagnostic. `NuGet.Packaging` 6.12.0 and `NuGet.Configuration` 6.12.0 enable reading package metadata from the local NuGet cache (no network calls) to create stub `SymbolNode`s for external package types. The 6.x line is the stable public Client SDK; 7.x is prerelease internal tooling. No other net-new dependencies are required.

**Core technologies (v1.2 additions only):**
- `Microsoft.Build.Locator` 1.11.2: MSBuild assembly discovery at process startup — required for `MSBuildWorkspace` in standalone executables; must be called before any MSBuild type is loaded
- `NuGet.Packaging` 6.12.0: Read `.nuspec` metadata from the local package cache for stub node construction — no network I/O
- `NuGet.Configuration` 6.12.0: Resolve global NuGet packages folder path without hardcoded paths; must match `NuGet.Packaging` major.minor

**Critical constraint:** `MSBuildLocator.RegisterDefaults()` must be the very first statement in `Program.cs`, before any `Microsoft.CodeAnalysis.MSBuild` or `Microsoft.Build.*` type is referenced. Failure mode is a silent MEF composition exception with no useful diagnostic. Do NOT add a direct `PackageReference` to `Microsoft.Build` — the locator manages those assemblies at runtime; referencing them directly causes binding failures.

**Already pinned (do not change):** `Microsoft.CodeAnalysis.CSharp` 4.12.0, `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0, `MessagePack` 3.1.4, `Lucene.Net` 4.8.0-beta00017, `ModelContextProtocol` 1.0.0, `System.IO.Hashing` 9.0.0, `OpenTelemetry.*` 1.15.0, all test packages.

### Expected Features

The v1.2 feature set is well-defined by the milestone scope. The dependency chain is clear: `.sln` ingestion and the `SolutionSnapshot` aggregate must land first because every other feature depends on them. Cross-project `SymbolEdge`s and unified BM25 search come next. The two highest-complexity features (NuGet stub nodes and per-project incremental re-ingestion) are P2 follow-ons.

**Must have (table stakes — P1):**
- `.sln` ingestion via `MSBuildWorkspace.OpenSolutionAsync` — skip non-C# projects gracefully with logged warning; `IngestionTools.ingest_project` already accepts `.sln` paths
- `SolutionSnapshot` extensions — `SolutionName?`, `IReadOnlyList<ProjectEntry> Projects` added to `SymbolGraphSnapshot`; `ProjectEntry` is a new record with name, file path, project references, and test project flag
- Cross-project `SymbolEdge`s — `Inherits`/`Implements`/`References` edges across project boundaries tagged with new `EdgeScope.CrossProject` enum value
- Per-node project attribution — `ProjectOrigin?` and `IsStub` fields added to `SymbolNode`; null/false defaults ensure backward compatibility with v1.0/v1.1 snapshots via MessagePack `ContractlessStandardResolver`
- Unified BM25 search index — `project` field added to each Lucene document; `search_symbols` gains optional `projectFilter` parameter
- `get_references` gains optional `crossProjectOnly` parameter — filters by `EdgeScope.CrossProject`
- `explain_solution` MCP tool — solution architecture overview: project list, dependency DAG, per-project node/edge counts, doc coverage, stub node count
- `ingest_solution` MCP tool — explicit re-ingestion trigger for `.sln` files; PathAllowlist-enforced

**Should have (P2 follow-on within milestone):**
- Stub/metadata nodes for NuGet packages — lightweight `IsStub = true` nodes from nuspec metadata; satisfies dangling edge targets without network I/O
- Per-project incremental re-ingestion — re-ingest only changed projects; manifest-of-manifests keyed by project path; most complex feature in the milestone

**Defer (v2+):**
- Vector/semantic search — `IVectorIndex` interface already stubbed; embeddings provider not yet selected
- HTTP/SSE MCP transport — auth model not yet designed
- Polyglot support (F#, VB) — deferred; F# Roslyn support incomplete
- Real-time file-watch re-ingestion — event storms and consistency complexity

**Anti-features (explicitly rejected):**
- Full NuGet package source ingestion — millions of symbols, gigabyte index bloat
- One giant merged `SymbolGraphSnapshot` replacing all project snapshots — loses per-project identity, breaks the determinism contract
- Per-project sub-snapshot storage — breaks the existing single-snapshot model that `KnowledgeQueryService`, `BM25SearchIndex`, and `SnapshotStore` all depend on

### Architecture Approach

The existing architecture already handles multi-project within one flat snapshot — `RoslynSymbolGraphBuilder` already loops over all projects in `ProjectInventory`. The v1.2 changes are additive: new fields on existing types (backward-compatible via MessagePack `ContractlessStandardResolver`), two new static pure-logic services, and one new MCP tool class. The `SchemaVersion` should be bumped from `"1.0"` to `"1.2"` in `RoslynSymbolGraphBuilder.BuildAsync` after all new fields are added; `KnowledgeQueryService` should tolerate both schema versions.

**New components:**
1. `SolutionDependencyResolver` (Ingestion) — static pure-logic class; topological sort (Kahn's algorithm) of the project dependency graph; produces dependency-ordered project list and `ProjectEntry[]`; easily tested with in-memory fixtures, no DI required
2. `SolutionGraphMerger` (Ingestion) — static pure-logic class; promotes stub nodes to real nodes when a real node with matching `SymbolId` appears from a later-processed project; tags `EdgeScope` on all edges; same pattern as `ChangeReviewer` and `SymbolGraphDiffer` from v1.1
3. `SolutionTools` (McpServer/Tools) — new `[McpServerToolType]` class implementing `explain_solution`; follows exact `DocTools`/`ChangeTools` pattern with `PathAllowlist` enforcement on all operations

**Modified components (key changes):**
- `SymbolNode` (Core/Symbols.cs): add `ProjectOrigin?` and `IsStub` — null/false defaults are backward-compatible
- `SymbolEdge` (Core/Symbols.cs): add `EdgeScope` enum with `IntraProject` default — backward-compatible
- `SymbolGraphSnapshot` (Core/Symbols.cs): add `SolutionName?` and `IReadOnlyList<ProjectEntry> Projects` — empty list default is backward-compatible
- `ProjectInventory` (Core/Abstractions.cs): add `ProjectDependencies` adjacency map
- `IKnowledgeQueryService` (Core/Abstractions.cs): add optional `projectFilter` to `SearchAsync`
- `LocalProjectSource` (Ingestion): populate `ProjectDependencies` adjacency map from Roslyn solution
- `RoslynSymbolGraphBuilder` (Ingestion): tag each node with `ProjectOrigin`; emit lightweight stub nodes for dangling edge targets
- `SnapshotManifestEntry` (Ingestion): add `SolutionName?` and `ProjectCount`
- `BM25SearchIndex` (Indexing): add `project` field to Lucene document
- `KnowledgeQueryService` (Indexing): thread `projectFilter` through `SearchAsync`
- `DocTools` (McpServer/Tools): add `projectFilter` to `search_symbols`, `crossProjectOnly` to `get_references`

### Critical Pitfalls

1. **MSBuildLocator not called before first MSBuild type load** — Call `MSBuildLocator.RegisterDefaults()` as the absolute first statement in `Program.cs`. This is the single most common `MSBuildWorkspace` failure mode. The error is a MEF composition exception with no useful diagnostic — it appears as an infrastructure failure, not a missing-package error.

2. **MSBuildWorkspace memory growth in long-running processes** — After `RoslynSymbolGraphBuilder.BuildAsync`, extract all needed data into `SymbolNode`/`SymbolEdge` plain records immediately and release `Compilation` objects. Do not cache `Compilation` or `SemanticModel` in DI services. Add a memory regression test: build the same fixture project 10 times in a loop and assert no unbounded Gen2 GC growth.

3. **MSBuildWorkspace silent failures on stale build artifacts** — `ProjectReference` resolution can produce semantically incomplete compilations if build artifacts are stale. Always check and log `workspace.Diagnostics` after `OpenSolutionAsync`. Treat any project with null or empty compilation as skipped.

4. **Non-deterministic snapshot serialization** — All collections in `SymbolGraphSnapshot` must be sorted before serialization (symbols by `SymbolId`, edges by `(From, To, Kind)`). The `SchemaVersion` bump to `"1.2"` must be accompanied by a byte-for-byte determinism test that runs the same fixture twice in the same process and asserts identical output.

5. **Stub node proliferation from transitive NuGet references** — Cap stub node creation to direct assembly references only (assemblies explicitly listed in the project file's `PackageReference` items), not the transitive closure. At 100 projects with rich NuGet graphs, uncapped stub node generation can produce tens of thousands of nodes and materially increase indexing time.

6. **STDIO contamination breaking MCP stream** — All `ILogger` sinks must write exclusively to `stderr` or a file sink. Any `Console.Write*` call in the server project, unhandled exception trace, or .NET startup banner written to stdout corrupts MCP JSON-RPC framing. This was established in v1.1 but must be verified in any new tool class or host modification.

---

## Implications for Roadmap

The bottom-up build order documented in ARCHITECTURE.md maps directly to implementation phases. Each phase is independently testable and delivers a verifiable increment. The recommended structure has 5 phases for the v1.2 milestone.

### Phase 1: Core Domain Model Extensions

**Rationale:** All v1.2 features depend on the extended domain types. These are the lowest-risk changes — adding nullable/default fields to existing records. Must land first because Ingestion, Indexing, and McpServer layers all reference these types. Zero behavior change in this phase; purely type contract updates.

**Delivers:** Updated `SymbolNode` (`ProjectOrigin?`, `IsStub`), updated `SymbolEdge` (`EdgeScope` with default), updated `SymbolGraphSnapshot` (`SolutionName?`, `Projects` list), new `ProjectEntry` record, new `EdgeScope` enum, updated `IKnowledgeQueryService.SearchAsync` signature, updated `ProjectInventory`

**Addresses:** Foundation for all P1 table-stakes features

**Avoids:** Backward-compatibility breaks — all new fields must have null/false/empty-list defaults; existing MessagePack artifacts from v1.0/v1.1 must deserialize without error

**Research flag:** Standard patterns — record type additions and MessagePack `ContractlessStandardResolver` backward-compat are well-understood. No additional research needed.

### Phase 2: Ingestion Layer — Solution-Aware Builder

**Rationale:** The core ingestion pipeline changes are the most complex and highest-risk in the milestone. Must come before indexing and tool layers. The two new static services (`SolutionDependencyResolver`, `SolutionGraphMerger`) are pure logic with no DI dependencies and are trivial to unit-test with in-memory fixtures before the full pipeline is wired.

**Delivers:** `SolutionDependencyResolver` (topological sort, `ProjectEntry` builder), updated `LocalProjectSource` (populates `ProjectDependencies`), updated `RoslynSymbolGraphBuilder` (tags `ProjectOrigin` per node, emits stub nodes for dangling targets), `SolutionGraphMerger` (stub promotion, edge scope tagging), updated `SnapshotManifestEntry`

**Uses:** `Microsoft.Build.Locator` 1.11.2 (startup call), `NuGet.Packaging` + `NuGet.Configuration` 6.12.0 (stub node metadata from local cache)

**Avoids:** MSBuildWorkspace memory growth (release `Compilation` objects immediately after node extraction); MSBuildLocator startup ordering pitfall (first call in Program.cs); silent workspace failures (log all `workspace.Diagnostics`); stub node proliferation (cap to direct references)

**Research flag:** May benefit from a spike test against the actual solution to characterize `workspace.Diagnostics` behavior and memory profile before committing to the stub node cap heuristic. Not blocking for planning, but recommended before implementation begins.

### Phase 3: Indexing Layer — Project-Aware Search

**Rationale:** Consumes the enriched `SymbolGraphSnapshot` from Phase 2. Short phase — adds a `project` field to the Lucene document schema and threads `projectFilter` through `KnowledgeQueryService`. No interface redesign; no new packages.

**Delivers:** Updated `BM25SearchIndex` (adds stored `project` Lucene field; routes `projectFilter` as a term query), updated `KnowledgeQueryService` (threads `projectFilter` through `SearchAsync`)

**Implements:** Unified BM25 search index spanning all projects (table-stakes P1 feature)

**Research flag:** Standard patterns — Lucene.Net field addition and term query routing are well-documented. No additional research needed.

### Phase 4: MCP Tool Surface — Solution-Aware Tools

**Rationale:** Consumes Phase 2 and Phase 3 outputs. Adds `projectFilter` and `crossProjectOnly` parameters to existing tools (low risk — new optional parameters; no existing client breaks) and adds the new `SolutionTools` class for `explain_solution`.

**Delivers:** Updated `DocTools` (`projectFilter` on `search_symbols`, `crossProjectOnly` on `get_references`), new `SolutionTools` class (`explain_solution` tool), `ingest_solution` tool registration (thin wrapper — `LocalProjectSource` already accepts `.sln` paths)

**Avoids:** STDIO contamination (all new tool logging to `stderr`); MCP tool surface expansion beyond read-only scope (`PathAllowlist` on all new tools, including `.sln` path validation); prompt injection via symbol names (sanitize all symbol data in tool responses)

**Research flag:** Standard patterns — follows established `DocTools`/`ChangeTools` pattern exactly. No additional research needed.

### Phase 5: P2 Enhancements — Stub Nodes and Incremental Re-ingestion

**Rationale:** These features have the most dependencies and the highest complexity. Stub node enrichment requires the full ingestion pipeline (Phase 2) to be stable and tested. Incremental per-project re-ingestion requires a manifest-of-manifests design on top of the existing `SnapshotStore` — a non-trivial design decision that benefits from observing real usage patterns in the stable core first.

**Delivers:** NuGet stub node enrichment via `PackageArchiveReader`/`NuspecReader`; per-project incremental re-ingestion with manifest-of-manifests

**Avoids:** Network I/O in tests (use `NuGet.Packaging` local cache reads only — never `NuGet.Protocol`); manifest-of-manifests breaking existing `SnapshotStore` content-hash scheme (design must be validated before implementation)

**Research flag:** The manifest-of-manifests design for incremental re-ingestion has no prior art in this codebase. Explicit design review of the `SnapshotStore` extension strategy is recommended before Phase 5 implementation begins. This is the only phase where additional research-phase work is warranted.

### Phase Ordering Rationale

- Core types must exist before Ingestion can use them; Ingestion must produce enriched snapshots before Indexing can store project-aware data; Indexing must support `projectFilter` before tools can expose it. This is a hard bottom-up dependency chain.
- Static pure-logic services (`SolutionDependencyResolver`, `SolutionGraphMerger`) are implemented and unit-tested before DI-wired components depend on them — consistent with the v1.1 pattern established by `ChangeReviewer` and `SymbolGraphDiffer`.
- P2 features are deferred to Phase 5 to avoid blocking the core milestone on the two highest-complexity features. The core solution graph must be validated by real ingestion runs before the incremental re-ingestion design is locked.
- Each phase boundary is a natural integration test point: the `SymbolGraphSnapshot` contract, the `BM25SearchIndex` document schema, and the MCP tool parameter signatures are all independently verifiable at each boundary.

### Research Flags

Phases with standard patterns (research-phase not needed):
- **Phase 1:** Record type additions and MessagePack backward-compat — well-established
- **Phase 3:** Lucene field addition and term query routing — well-documented
- **Phase 4:** Follows existing `DocTools`/`ChangeTools` pattern exactly

Phases that may benefit from targeted investigation before or during implementation:
- **Phase 2:** A spike test characterizing MSBuildWorkspace memory profile and `workspace.Diagnostics` behavior on the actual solution is recommended before committing to the stub node cap heuristic. Not blocking for roadmap planning.
- **Phase 5:** Manifest-of-manifests design for incremental re-ingestion needs explicit design validation before implementation. This is the only phase that genuinely warrants a research-phase stop.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All three new packages verified on NuGet Gallery; `MSBuildLocator` usage verified via official Roslyn team gist (Dustin Campbell); `NuGet.Packaging` 6.x confirmed as stable public SDK line per Microsoft Learn |
| Features | HIGH | Feature set derived directly from codebase inspection + PROJECT.md milestone definition; dependency chain verified through source code; no assumptions required |
| Architecture | HIGH | Based on direct inspection of all 6 project layers and all relevant source files; change surface identified at file and member level; anti-patterns derived from Roslyn GitHub issues |
| Pitfalls | HIGH (Roslyn/MSBuild) / MEDIUM (NuGet stub) | MSBuildWorkspace memory, locator startup, and workspace diagnostics pitfalls verified via GitHub issues and official Roslyn docs; NuGet stub node proliferation heuristic is an inference from documented ecosystem behavior |

**Overall confidence:** HIGH

### Gaps to Address

- **MSBuildWorkspace at scale:** Memory profile and `workspace.Diagnostics` behavior with 50+ project solutions is not well-documented. Recommend a one-day spike against the actual solution before finalizing the stub node cap heuristic in Phase 2. This is an implementation-time gap, not a planning blocker.
- **Manifest-of-manifests design for incremental re-ingestion:** No prior art in this codebase. The existing `SnapshotStore` uses a single `manifest.json` keyed by `ContentHash`. A multi-project manifest design needs explicit design review before Phase 5 implementation begins to avoid breaking the content-hash scheme.
- **`SchemaVersion` migration policy:** The research recommends bumping `SchemaVersion` to `"1.2"` and tolerating both `"1.0"` and `"1.2"` artifacts in `KnowledgeQueryService`. The exact behavior for v1.0 artifacts — re-ingest required, auto-upgrade on read, or serve as-is with missing fields — must be decided before Phase 2 ships. Current recommendation: serve v1.0 artifacts as-is with `ProjectOrigin = null` on all nodes (the null default is safe); require explicit `ingest_solution` call to get v1.2 enriched data.

---

## Sources

### Primary (HIGH confidence)
- [NuGet Gallery: Microsoft.Build.Locator 1.11.2](https://www.nuget.org/packages/Microsoft.Build.Locator/) — version confirmed, latest stable Nov 2025
- [NuGet Gallery: NuGet.Packaging 6.12.0](https://www.nuget.org/packages/NuGet.Packaging/6.12.0) — stable client SDK line confirmed
- [Using MSBuildWorkspace — Dustin Campbell (Roslyn team)](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3) — authoritative usage guide; MEF pitfall; `ExcludeAssets=runtime` pattern
- Direct codebase inspection: `DocAgent.Core/Symbols.cs`, `DocAgent.Core/Abstractions.cs`, `DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs`, `LocalProjectSource.cs`, `SnapshotStore.cs`, `DocAgent.Indexing/KnowledgeQueryService.cs`, `BM25SearchIndex.cs`, `DocAgent.McpServer/Tools/DocTools.cs`, `IngestionTools.cs`, `ChangeTools.cs`, `IngestionService.cs`

### Secondary (MEDIUM confidence)
- [NuGet Client SDK — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk) — `NuGet.Packaging` + `NuGet.Configuration` API documentation
- [MSBuildWorkspace cross-project reference issue #36072](https://github.com/dotnet/roslyn/issues/36072) — `ProjectReference` loading behavior
- [dotnet/roslyn #25921](https://github.com/dotnet/roslyn/issues/25921) — workspace diagnostic behavior
- [Steve Gordon — Using Roslyn APIs to Analyse a .NET Solution](https://www.stevejgordon.co.uk/using-the-roslyn-apis-to-analyse-a-dotnet-solution) — solution analysis patterns
- [NuGet Gallery: Microsoft.CodeAnalysis.Workspaces.MSBuild 4.12.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/) — confirmed already in CPM

---

*Research completed: 2026-03-01*
*Ready for roadmap: yes*
