# Project Research Summary

**Project:** DocAgentFramework v2.5 — NuGet Package Mapping
**Domain:** .NET symbol graph server — additive milestone adding NuGet dependency graph + DLL reflection capabilities
**Researched:** 2026-03-26
**Confidence:** HIGH

## Executive Summary

DocAgentFramework v2.5 adds a lateral capability branch to the existing pipeline: after a solution is ingested, agents can query the NuGet dependency graph and cross-reference package-exported types against source code references. The core mechanisms are well-understood and reuse what is already in the project — `NuGet.ProjectModel` and `NuGet.Configuration` are the only new packages needed, and DLL reflection reuses the Roslyn `MetadataReference.CreateFromFile` API already present at 4.14.0. The recommended architecture keeps `PackageGraph` separate from `SymbolGraphSnapshot`, runs stub enrichment transparently inside `SolutionIngestionService`, and exposes two new MCP tools (`get_dependencies`, `find_package_usages`) that follow the established PathAllowlist and response-format patterns exactly.

The primary risk cluster is in the dependency-source selection and DLL resolution layers. `packages.lock.json` is opt-in and absent from most repos including this one; `project.assets.json` (always present after restore) must be the primary source. NuGet cache path resolution requires `SettingsUtility.GetGlobalPackagesFolder()` rather than a hardcoded path to survive CI environments and custom configurations. DLL TFM selection requires a best-fit compatibility walk because many packages ship only `netstandard2.0` and have no `net10.0` subfolder. All three of these are correctness requirements that produce silent empty results when wrong — not clear errors.

The second risk cluster is memory management and matching correctness. Loading many DLLs via `MetadataReference.CreateFromFile` without an `AssemblyMetadata` cache produces a documented 250MB-per-call native heap growth in long-running server processes. Stub-to-reflected-type matching on display-string FQNs (e.g., `IEnumerable<T>`) produces zero matches for every generic type; the correct join key is `(ContainingAssemblyName, MetadataName)` using the CLR backtick format (`IEnumerable\`1`). Both must be addressed in the initial design, not retrofitted. The component build order is deterministic, parallel development opportunities are clear, and the existing test infrastructure (PipelineOverride seams, pure-static parser pattern, warning accumulation) provides solid attachment points for every new component.

## Key Findings

### Recommended Stack

The existing stack (Roslyn 4.14.0, Lucene.Net 4.8-beta, MessagePack 3.1.4, MCP SDK 1.0.0, Aspire 13.1.2) requires only two new NuGet packages. `NuGet.ProjectModel 7.3.0` provides `PackagesLockFileFormat.Read()` — the only correct parser for a format with three schema versions (V1/V2/V3). `NuGet.Configuration 7.3.0` provides `SettingsUtility.GetGlobalPackagesFolder()` for cross-platform cache path resolution that respects the full four-level override chain. Both packages must be pinned at matching versions and added only to `DocAgent.Ingestion.csproj`. DLL reflection uses `AssemblyMetadata.CreateFromFile` (IDisposable) from the existing Roslyn 4.14.0 reference — no additional PE reader is needed.

**Core technologies (new additions only):**
- `NuGet.ProjectModel 7.3.0`: `packages.lock.json` parsing — first-party parser, handles all three format versions including v2 (CPM mode used by this repo)
- `NuGet.Configuration 7.3.0`: NuGet cache path resolution — handles `NUGET_PACKAGES` env var, `nuget.config` overrides, CI environments; must version-match ProjectModel
- `AssemblyMetadata.CreateFromFile` (Roslyn 4.14.0, existing): DLL reflection — IDisposable pattern prevents native heap leak; same API surface as existing `RoslynSymbolGraphBuilder`
- `Microsoft.CodeAnalysis.CSharp` (4.14.0, existing): Must be added as direct reference to `DocAgent.Ingestion.csproj` — currently only in McpServer

**What NOT to add:** `Mono.Cecil`, `dnlib`, `System.Reflection.MetadataLoadContext` (redundant with Roslyn), `NuGet.Protocol` direct reference (already a transitive dep), `NuGet.Commands` / `NuGet.PackageManagement` (wrong abstraction level).

### Expected Features

All v2.5 features are P1 (must-ship). The dependency source chain (`project.assets.json` primary, `packages.lock.json` supplement) is the foundation. `PackageGraph` is the core domain type. The two MCP tools are the user-visible deliverables. Stub node enrichment (`NodeKind.Enriched`) is the mechanism that makes `find_package_usages` produce meaningful results.

**Must have (table stakes — v2.5):**
- `project.assets.json` as primary dependency source with `packages.lock.json` as optional supplement — most repos do not opt in to lock files; `project.assets.json` is always present after restore
- `PackageGraph` domain type in `DocAgent.Core` — structured, MessagePack-compatible dependency graph; NOT embedded in `SymbolGraphSnapshot`
- DLL path resolution from NuGet global cache with TFM best-fit walk — required for any package predating .NET 10 (affects nearly every package)
- Roslyn `AssemblyMetadata` reflection to extract public API surface — produces type metadata for stub enrichment; cached by `(packageId, version, tfm)`
- `PackageOrigin` field on `SymbolNode` (appended, MessagePack compat) — enables fast package-to-symbol grouping in `find_package_usages`
- Stub node enrichment pass (`NodeKind.Stub` → `NodeKind.Enriched`) — upgrades bare stubs with real type info; join key is `(AssemblyName, MetadataName)` tuples
- `get_dependencies` MCP tool — query the dependency graph; PathAllowlist on tool parameters
- `find_package_usages` MCP tool — cross-reference package exports with source references; requires enriched stubs with `PackageOrigin`
- DLL path security validation — every derived DLL path validated against NuGet cache root before `CreateFromFile`
- Graceful diagnostic when `project.assets.json` absent — structured message, not exception; suggest `dotnet restore`

**Should have (v2.5.x after validation):**
- Dependency path explanation ("why is X a transitive dep") — BFS over `PackageGraph` edges; useful for diagnosing security advisories
- Snapshot-level package version diff in `diff_solution_snapshots` — extends existing ChangeTools pattern to the package dimension

**Defer (v3+):**
- npm/TypeScript package mapping — different parsing approach entirely; deferred per PROJECT.md
- `PackageReflectionCache` as persistent sidecar — only if reflection performance is a measured problem at scale

**Anti-features (do not build):**
- Downloading packages from nuget.org at runtime — violates "no implicit network calls" constraint; breaks air-gapped environments
- Eager full transitive DLL reflection at ingestion time — reflecting 200+ packages per solution makes ingestion take minutes
- `enrich_stubs` or `reflect_packages` as user-facing MCP tools — enrichment is an internal optimization pass, not a user operation

### Architecture Approach

v2.5 runs as a parallel branch alongside the existing pipeline rather than extending it. `SolutionIngestionService` (after the per-project stub synthesis pass) drives `LockFileParser` → `NuGetCacheReflector` → `StubNodeEnricher` conditionally when dependency sources are found. `PackageGraph` flows out via a new `PackageGraphs` field on the existing `SolutionIngestionResult` structure, is cached in a new `PackageQueryService`, and is never embedded in `SymbolGraphSnapshot`. The only Indexing layer change is a two-line filter update in `BM25SearchIndex` to include `NodeKind.Enriched`.

**Major components (all new unless noted):**
1. `PackageTypes.cs` in `DocAgent.Core` — `PackageEntry`, `PackageGraph`, `AssemblyMapping` pure domain types; no IO; unblocks everything else
2. `LockFileParser.cs` in `DocAgent.Ingestion` — pure static parser following existing `XmlDocParser` pattern; primary source is `project.assets.json` resolved versions
3. `NuGetCacheReflector.cs` in `DocAgent.Ingestion` — DLL location + `AssemblyMetadata` reflection with TFM best-fit walk and bounded cache; critical path component
4. `StubNodeEnricher.cs` in `DocAgent.McpServer/Ingestion` — matches stubs to reflected types on `(AssemblyName, MetadataName)` tuples; replaces `NodeKind.Stub` with `NodeKind.Enriched`
5. `PackageQueryService.cs` in `DocAgent.McpServer` — `ConcurrentDictionary` cache of `PackageGraph` per snapshot hash; populated by ingestion, queried by tools
6. `PackageTools.cs` in `DocAgent.McpServer/Tools` — `get_dependencies` and `find_package_usages`; PathAllowlist + DLL path validation
7. `BM25SearchIndex.cs` (MODIFIED) — two-line filter change to include `NodeKind.Enriched` alongside `NodeKind.Real`
8. `SolutionIngestionService.cs` (MODIFIED) — call enrichment pipeline post-stub-synthesis; add `PipelineOverride` seams for testability

**Key patterns to follow:**
- Pure static parsers (no DI, no IO abstractions) — matches `XmlDocParser`; testable with fixture files, no mocking needed
- Soft failure with `List<string> warnings` accumulator — missing DLL = warning, not exception; matches existing `SolutionIngestionService` pattern
- `PipelineOverride` seams for enrichment — matches existing MSBuild-free test isolation pattern
- `AssemblyMetadata` (IDisposable) + cache keyed on `(packageId, version, tfm)` capped at ~200 entries — prevents native heap growth
- `NodeKind.Enriched = 2` appended to enum — preserves MessagePack backward compatibility per project memory constraint

### Critical Pitfalls

1. **`packages.lock.json` absent from most repos including this one** — use `project.assets.json` as the primary dependency source; `packages.lock.json` is opt-in via `RestorePackagesWithLockFile=true` which this repo does not set. Return a structured diagnostic when neither file exists rather than silently returning empty data.

2. **TFM key in `packages.lock.json` is `.NETCoreApp,Version=v10.0`, not `net10.0`** — use `NuGetFramework.Parse().DotNetFrameworkName` to normalize before dictionary lookup; hand-crafted test fixtures will pass while real lock files from `dotnet restore` fail silently.

3. **DLL TFM best-fit walk required — direct TFM substitution breaks for `netstandard2.0`-only packages** — enumerate `ref/` then `lib/` subfolders, filter by `DefaultCompatibilityProvider.IsCompatible`, take the highest compatible match. Affects `YamlDotNet`, `MessagePack`, and nearly every package predating .NET 10.

4. **`MetadataReference.CreateFromFile` leaks 250MB native heap per ingestion cycle** — use `AssemblyMetadata.CreateFromFile` (IDisposable) with a bounded cache keyed on `(packageId, version, tfm)`; this is an architectural decision that is hard to add retrospectively. Documented in dotnet/runtime issue #13301.

5. **Stub-to-reflected-type join on display-string FQN produces zero matches for all generic types** — join on `(ContainingAssemblyName, MetadataName)` tuples using `ITypeSymbol.MetadataName` (CLR format: `IEnumerable\`1`) not `ToDisplayString()` (`IEnumerable<T>`). The existing `SolutionIngestionService.MaybeAddStubNode` at line 544–571 uses `ToDisplayString()` for FQN — wrong key for enrichment.

6. **DLL paths derived from lock/assets files bypass the existing PathAllowlist** — validate every derived DLL path is under `GetGlobalPackagesFolder()` result before calling `CreateFromFile`; also filter reflected types to `Type.IsPublic` / `Type.IsNestedPublic` only to prevent internal type leakage.

7. **Assembly version conflicts when loading all transitive DLLs independently** — derive DLL paths exclusively from `project.assets.json` resolved versions (not the raw package list), to load exactly one DLL per unique assembly name per project.

## Implications for Roadmap

The component build order is deterministic from dependency analysis. Steps 2 and 3 can develop in parallel after Step 1. The critical path runs through `NuGetCacheReflector` (most complex new component). Research flags below identify where pitfall density is highest and verification effort is non-trivial.

### Phase 1: Domain Types + Dependency Source Parsing

**Rationale:** `PackageTypes.cs` in `DocAgent.Core` unblocks every other component. Dependency source selection (primary: `project.assets.json`, secondary: `packages.lock.json`) is the most pitfall-dense area and must be established before any other parsing logic is written. Getting the TFM key format wrong here causes all downstream data to be silently empty.

**Delivers:** `PackageEntry`, `PackageGraph`, `AssemblyMapping` types; `NodeKind.Enriched = 2` enum value in `Symbols.cs`; `LockFileParser` (pure static, tested with real `project.assets.json` output from this repo's `obj/` directory); `packages.lock.json` secondary parser with v2 CPM format support; graceful diagnostic when neither file is found.

**Addresses features:** Lock file parsing, `project.assets.json` fallback, `PackageGraph` domain type, `PackageOrigin` field scaffold.

**Avoids pitfalls:** `packages.lock.json` absent (#1), TFM key format (#2), missing assets file diagnostic.

### Phase 2: DLL Path Resolution + AssemblyMetadata Cache

**Rationale:** DLL resolution and the `AssemblyMetadata` cache must be designed as a unit — the cache architecture (keyed on `(packageId, version, tfm)`, bounded to ~200 entries, IDisposable disposal on eviction) is an architectural decision that is hard to retrofit. TFM best-fit walk and NuGet cache root resolution via `SettingsUtility` are correctness prerequisites. Security validation of derived DLL paths must be wired here before any DLL is ever loaded.

**Delivers:** `NuGetCacheReflector.cs` with TFM best-fit walk (enumerate `ref/` then `lib/` subfolders, filter by compatibility), `AssemblyMetadata` bounded cache, NuGet cache root resolution via `NuGet.Configuration.SettingsUtility`, DLL path security validator (`IsUnder(GetGlobalPackagesFolder())` check), `NuGetCachePath` config option on `DocAgentServerOptions`. `Microsoft.CodeAnalysis.CSharp` added to `DocAgent.Ingestion.csproj`.

**Uses:** `NuGet.ProjectModel 7.3.0`, `NuGet.Configuration 7.3.0` (both added to `Directory.Packages.props` and `DocAgent.Ingestion.csproj`), Roslyn 4.14.0 `AssemblyMetadata`.

**Addresses features:** DLL path resolution, TFM best-match, DLL reflection public API extraction.

**Avoids pitfalls:** NuGet cache path hard-coding (#3), DLL TFM best-fit (#4), MetadataReference memory leak (#5), DLL path security bypass (#6), assembly version conflicts (#7).

### Phase 3: Stub Enrichment + BM25 Index Update

**Rationale:** With reflected symbols available from Phase 2, stub node enrichment can be built and unit-tested in isolation before pipeline integration. The `(AssemblyName, MetadataName)` join key and the generic type matching issue are best validated against real stub nodes produced by the existing `SolutionIngestionService` — not fabricated fixtures — before any pipeline wiring.

**Delivers:** `StubNodeEnricher.cs` with pre-built `Dictionary<(string assembly, string metadataName), SymbolNode>` matching index; `NodeKind.Enriched` nodes replacing matched stubs with real type info; `PackageOrigin` field populated on enriched nodes; one-line `BM25SearchIndex.cs` filter update to include `NodeKind.Enriched`. Development/test packages filtered out (by `PrivateAssets="All"` flag from assets file).

**Addresses features:** Stub node enrichment, `PackageOrigin` field on `SymbolNode`.

**Avoids pitfalls:** Generic type FQN mismatch (#5), O(n×m) enrichment scan without index (performance trap), dev-dependency package reflection inflation.

### Phase 4: Pipeline Integration + Service Wiring

**Rationale:** Once all components are independently tested, integrate into `SolutionIngestionService`. The `PipelineOverride` seams allow the test suite to remain free of real NuGet cache access. `SolutionIngestionResult` gets its `PackageGraphs` field. `PackageQueryService` is registered and populated via DI.

**Delivers:** Modified `SolutionIngestionService.cs` (enrichment pipeline called post-stub-synthesis, with `LockFileParserOverride` and `ReflectorOverride` seams); modified `SolutionIngestionResult.cs` (`PackageGraphs: Dictionary<string, PackageGraph>` field); `PackageQueryService.cs` (in-memory `ConcurrentDictionary` cache, `StorePackageGraphs` / `GetPackageGraphs`); DI registration in `ServiceCollectionExtensions.cs`.

**Implements architecture components:** `StubNodeEnricher` → `SolutionIngestionService` integration, `PackageQueryService` cache.

### Phase 5: MCP Tools + Security Integration

**Rationale:** MCP tools are the user-visible deliverable and come last because they depend on all other components. PathAllowlist enforcement on tool parameters plus the DLL path validator from Phase 2 must both be active. Response format (json/markdown/tron) must match existing tools exactly.

**Delivers:** `PackageTools.cs` with `get_dependencies` (returns `PackageGraph` for a project) and `find_package_usages` (traverses External edges to find source symbols referencing package types); PathAllowlist on snapshot-store directory (same pattern as `SolutionTools`); rate-limiting bucket assignment (query bucket for both tools); `CLAUDE.md` update documenting the two new tools.

**Addresses features:** `get_dependencies` MCP tool, `find_package_usages` MCP tool, PathAllowlist security, internal type filtering.

**Avoids pitfalls:** DLL path security bypass (#6), internal type leakage in reflected results.

### Phase Ordering Rationale

- Phase 1 before everything: `PackageEntry`/`PackageGraph` types are consumed by every other component; correct dependency source selection prevents silent data failures that are hard to diagnose after downstream code is written.
- Phase 2 before Phase 3: Reflection must produce symbols before enrichment can match them; the `AssemblyMetadata` cache architecture must be established before the enricher depends on it.
- Phase 3 before Phase 4: Stub enrichment must be unit-tested in isolation (against real existing stub nodes) before pipeline integration; generic type matching is easiest to catch in a focused unit test, not an integration test.
- Phase 4 before Phase 5: Tools depend on `PackageQueryService` which depends on ingestion wiring.
- Phases 2 and 3 can begin in parallel once Phase 1 types compile — `NuGetCacheReflector` and `StubNodeEnricher` are independent modules with a clean interface boundary.
- Step 8 (`BM25SearchIndex` filter update) requires only `NodeKind.Enriched` from Phase 1 and is independent of Phases 2–4.

### Research Flags

Phases needing careful verification during execution (pitfall density high, non-trivial edge cases):

- **Phase 1:** Verify TFM key normalizer against a real `packages.lock.json` generated by `dotnet restore --use-lock-file` on this repo (not a hand-crafted fixture). Confirm v2 CPM format key is `.NETCoreApp,Version=v10.0`, not `net10.0`. Verify `project.assets.json` parsing produces the correct resolved version per package.
- **Phase 2:** Test DLL TFM best-fit against `YamlDotNet 16.3.0` (ships `netstandard2.0` only) and confirm correct DLL found for a `net10.0` project. Test with `NUGET_PACKAGES` env var pointing to a non-default path. Run 10 consecutive ingestion cycles and confirm process RSS is stable after the first cycle (proving cache works).
- **Phase 3:** Verify generic type matching with `IEnumerable<T>`, `Task<TResult>`, `IReadOnlyDictionary<TKey, TValue>` stub nodes produced by the existing `SolutionIngestionService` against this solution — not fabricated stubs.

Phases with well-established patterns (standard execution):

- **Phase 4:** Pipeline wiring follows existing `PipelineOverride` seam pattern exactly; low risk once individual components are tested.
- **Phase 5:** MCP tool structure is copy-adapt from `SolutionTools.cs`; PathAllowlist pattern is established. No novel patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | NuGet package versions verified on NuGet.org (2026-02-10 release date confirmed); Roslyn `AssemblyMetadata` API stable since Roslyn 1.x; direct reads of `Directory.Packages.props` and project files confirm current versions and what needs adding |
| Features | HIGH | Feature scope derived from `PROJECT.md` and confirmed against existing codebase direct reads; feature dependencies traced from existing `SolutionIngestionService` stub synthesis code; anti-features clearly motivated by existing project constraints |
| Architecture | HIGH | Based on direct reads of all affected source files: `SolutionIngestionService.cs`, `BM25SearchIndex.cs`, `SolutionTools.cs`, `SnapshotStore.cs`, `Symbols.cs`; all integration points verified against actual code; build order dependencies are deterministic |
| Pitfalls | HIGH | Lock file format from NuGet wiki + official docs; MetadataReference memory behavior from Roslyn docs and dotnet/runtime issue #13301 (250MB figure sourced); TFM key mismatch from NuGet/Home issues #10257 and #10901; stub FQN analysis from direct inspection of `SolutionIngestionService.MaybeAddStubNode` lines 544–571 |

**Overall confidence:** HIGH

### Gaps to Address

- **TFM compatibility table scope:** Research recommends `NuGet.Frameworks.DefaultCompatibilityProvider` for correct compatibility checks but notes a lightweight static table for the `net10.0 → netstandard2.0` descent is sufficient for v2.5. The exact threshold where the static table breaks (unusual TFMs, RID-specific packages) is an implementation detail — validate against the actual packages in this project's dependency graph during Phase 2 execution.
- **`ref/` vs `lib/` folder preference for v2.5:** Research identifies `ref/` should be preferred for compile-time API surface accuracy, but also flags this as an acceptable known limitation for v2.5 (fix before v3.0). Decision: use `lib/` for v2.5, document the limitation in `PackageTools` response metadata.
- **`project.assets.json` format stability:** The assets file schema is an MSBuild/NuGet internal format. If the schema changes in a future .NET SDK, the parser will break silently. Mitigate with integration tests that run against the actual restored assets from this project's `obj/` directory (not a committed fixture).

## Sources

### Primary (HIGH confidence)
- `NuGet.ProjectModel 7.3.0` on NuGet.org — version and 2026-02-10 release date confirmed
- `NuGet.Configuration 7.3.0` on NuGet.org — version confirmed; versioning cadence alignment with ProjectModel
- [NuGet Client SDK reference — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/reference/nuget-client-sdk) — `SettingsUtility.GetGlobalPackagesFolder()` API
- [NuGet global packages and cache folders — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders) — four-level override chain, platform defaults (updated 2026-03-03)
- [NuGet lock file implementation spec — NuGet/Home Wiki](https://github.com/NuGet/Home/wiki/Repeatable-build-using-lock-file-implementation) — TFM key format, v1/v2/v3 schema, `type: Direct/Transitive`
- [packages.lock.json uses incorrect TFM — NuGet/Home #10257](https://github.com/NuGet/Home/issues/10257) — TFM key inconsistency between spec and generated output
- [packages.lock.json broken with OS-specific TFMs — NuGet/Home #10901](https://github.com/NuGet/Home/issues/10901) — TFM key format variations across NuGet versions
- [NuGet dependency resolution — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) — `project.assets.json` as authoritative resolved graph
- [Select assemblies referenced by projects — Microsoft Learn](https://learn.microsoft.com/en-us/nuget/create-packages/select-assemblies-referenced-by-projects) — `ref/` vs `lib/` folder preference
- [AssemblyMetadata.CreateFromFile — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.assemblymetadata.createfromfile) — IDisposable pattern, module lifecycle
- [MetadataReference.CreateFromFile — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.metadatareference.createfromfile) — native heap behavior
- [Memory leak using MetadataReferences — dotnet/runtime #13301](https://github.com/dotnet/runtime/issues/13301) — documented 250MB-per-call native heap growth
- [PackagesLockFileFormat.cs — NuGet.Client source](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.ProjectModel/ProjectLockFile/PackagesLockFileFormat.cs) — `Read()` overloads and parsing logic
- Direct codebase: `SolutionIngestionService.MaybeAddStubNode` lines 544–571 — stub nodes use `ITypeSymbol.OriginalDefinition.ToDisplayString()` for FQN
- Direct codebase: `DocAgent.Core/Symbols.cs` lines 115–128 — `NodeKind`, `EdgeScope`, `SymbolNode` record shape
- Direct codebase: `Directory.Packages.props` — `ManagePackageVersionsCentrally=true`, no `RestorePackagesWithLockFile`, no `packages.lock.json` files present

### Secondary (MEDIUM confidence)
- [Compiling with Roslyn without memory leaks — Carl Johansen blog](https://carljohansen.wordpress.com/2020/05/09/compiling-expression-trees-with-roslyn-without-memory-leaks-2/) — `AssemblyMetadata` disposal strategy and collectible ALC pattern
- [dotnet nuget why command — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-why) — dependency path explanation reference for v2.5.x feature

---
*Research completed: 2026-03-26*
*Ready for roadmap: yes*
