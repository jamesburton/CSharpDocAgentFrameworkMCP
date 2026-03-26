# Feature Research

**Domain:** NuGet package dependency graph + DLL reflection + MCP tooling (additive milestone to DocAgentFramework v2.5)
**Researched:** 2026-03-26
**Confidence:** HIGH — core mechanisms (lock file parsing, Roslyn MetadataReference, NuGet cache layout) are well-documented; feature scope derived from PROJECT.md and confirmed against existing codebase

---

## Scope Note

This is a subsequent milestone (v2.5) research document. The 15 existing MCP tools, solution ingestion, and stub node pipeline are already shipped. Research focuses exclusively on what is needed to expose NuGet package dependency graphs and DLL-reflected public API surfaces through new MCP tools.

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that define minimum useful behavior for a "NuGet package mapping" milestone. Missing any of these makes the milestone feel incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Parse `packages.lock.json` for direct + transitive deps | Lock file is the authoritative pinned graph. TFM-keyed sections with `"Direct"` and `"Transitive"` type fields, resolved version, contentHash, and per-package dependency list. Every .NET project with `RestorePackagesWithLockFile=true` generates one. | LOW | JSON deserialize only — no MSBuild required. May not exist on every repo (opt-in property). |
| Fallback to `project.assets.json` when lock file absent | `project.assets.json` is generated on every `dotnet restore` in `obj/`. Many repos do not opt in to `packages.lock.json`. Without fallback, the tool fails silently for most repos. | LOW | `project.assets.json` has a richer but noisier format. Parse the `libraries` section for package id/version/type. |
| `PackageGraph` domain type | Structured representation of the dependency tree — package id, version, type (Direct/Transitive), assembly file paths per TFM, dependency links to other packages. Needed by both tools. | LOW | New type in DocAgent.Core. MessagePack-serializable. NOT embedded in SymbolGraphSnapshot — parallel metadata. |
| DLL path resolution from NuGet global packages cache | Agents cannot query package APIs without knowing where the DLL lives. Path is deterministic: `{globalPackagesFolder}/{id}/{version}/lib/{tfm}/{assembly}.dll`. Default root: `~/.nuget/packages` (cross-platform). Overridable via `NUGET_PACKAGES` env var or `globalPackagesFolder` NuGet config. | LOW | TFM selection requires best-match logic (net10.0 descends to netstandard2.1 when exact TFM folder absent). |
| TFM best-match DLL resolution | Many packages ship only `netstandard2.0` or `netstandard2.1` folders. A `net10.0` project uses them. Without best-match, DLL resolution fails silently for those packages. | MEDIUM | Hardcode the common compatibility descent order for net10.0: net10.0 → net9.0 → net8.0 → netstandard2.1 → netstandard2.0 → net461. NuGet.Frameworks package has CompatibilityTable but adds a new dep. |
| Extract public API surface via Roslyn MetadataReference | This is how stub nodes get real content. Pattern: `MetadataReference.CreateFromFile(dllPath)` → add to throwaway `CSharpCompilation` → `compilation.GetAssemblyOrModuleSymbol(ref)` as `IAssemblySymbol` → walk `GlobalNamespace` with `SymbolVisitor`. Filter to `Accessibility.Public` / `Accessibility.Protected` only. | MEDIUM | One throwaway compilation per assembly. Must skip compiler-generated types. Parallel.ForEach over namespace members is safe. |
| Enrich existing stub nodes with real type info from DLL reflection | Stub nodes (NodeKind.Stub) currently carry only FQN + minimal metadata. After reflection they should carry full DisplayName, ReturnType, Parameters, GenericConstraints, Accessibility — same fields as real nodes. Stub identity must be preserved (same SymbolId) so existing External edges remain valid. | MEDIUM | Stub node replacement in the flat graph. Post-walk pass in SolutionIngestionService or a new StubEnrichmentService. |
| `PackageOrigin` field on SymbolNode | Annotates each enriched SymbolNode with the NuGet package id + version that provides it. Enables `find_package_usages` to group references by package without full graph traversal. | LOW | Append at end of SymbolNode record (MessagePack backward compat rule). |
| `get_dependencies` MCP tool | Returns PackageGraph for a project — direct + transitive packages, resolved versions, TFM, assembly mappings. Analogous to `dotnet nuget why` (available since .NET 8.0.4xx SDK). Agents need to ask "what does this project depend on?" before any cross-reference query. | LOW | PathAllowlist enforcement on input path. Reads lock file at rest — no MSBuild invocation. Return json/markdown/tron consistent with existing tools. |
| `find_package_usages` MCP tool | Given a package name (or specific type FQN), returns all source code references from the indexed symbol graph. Traverses External-scoped edges in the snapshot, matches stub nodes belonging to the package's assemblies (via PackageOrigin), collects referencing real nodes. | MEDIUM | Requires enriched stubs with PackageOrigin. Returns same response shape as `get_references`. |
| PathAllowlist security on PackageTools | Established pattern for every tool class. Reject out-of-allowlist paths with opaque not_found response. No error detail leakage. | LOW | Copy existing pattern from DocTools/ChangeTools/SolutionTools. |

### Differentiators (Competitive Advantage)

Features beyond minimum that increase agent utility but are not assumed to exist.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Dependency path explanation ("why is X here?") | For a given package, return the shortest path from a direct dependency to it through the transitive chain. Analogous to `dotnet nuget why`. Useful for diagnosing unexpected deps or security advisories. | MEDIUM | BFS over PackageGraph edges. No external library needed. |
| Snapshot-level package version diff | Given two snapshots, show which NuGet packages changed version (added, removed, bumped). Extends the existing `diff_snapshots` / `diff_solution_snapshots` pattern to the package dimension. | MEDIUM | Requires both snapshots to carry PackageGraph metadata. Aligns with ChangeTools philosophy. |
| On-demand (lazy) reflection mode | Defer DLL reflection until `find_package_usages` is first called rather than doing it at ingestion time. Avoids slowing `ingest_solution` by reflecting 200+ packages eagerly. | MEDIUM | PackageReflectionCache alongside SnapshotStore, keyed by content hash. |
| contentHash verification against lock file | Lock file records SHA-512 contentHash per package. Verify it against the DLL in cache before reflecting. Detects corrupted or tampered local caches. | LOW | Optional validation step. Log warning on mismatch, do not fail hard. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Download packages from nuget.org at runtime | "What if the package isn't in the local cache?" | Introduces network dependency. Breaks air-gapped environments. Violates "No implicit network calls in tests" constraint. Security risk (package substitution). | Fail gracefully: "Package not in local cache — run `dotnet restore` first." |
| Eager full transitive DLL reflection at ingestion time | Comprehensive coverage | 200+ packages on a real solution. Reflecting every DLL at `ingest_solution` time makes it take minutes. Creates massive node count. | Lazy/on-demand reflection. Reflect when `find_package_usages` is called or when agent requests enrichment explicitly. |
| NuGet HTTP API queries for package metadata (license, README, deprecated flags) | Rich package provenance data | External HTTP in a tool designed for offline compiler-grade analysis. Adds latency, failure modes, auth complexity. | Stick to local cache data. NuGet.org metadata is out of scope for v2.5. |
| npm/TypeScript package mapping | Symmetry with .NET | Explicitly deferred in PROJECT.md. Node module resolution is different (node_modules, package.json, barrel files). Different parsing approach entirely. | Keep deferred. Do .NET right first. |
| Store full DLL reflection results inside SymbolGraphSnapshot | Persistent enrichment | Massively inflates snapshot size. Snapshot determinism would require DLL content hashes. Breaks "same input = identical output" if cache layout changes between runs. | Enrich stubs in-memory at ingestion; persist enriched nodes in the snapshot but not the raw reflection data. |
| Vulnerability scanning / CVE integration | Package version awareness creates opportunity | Different domain entirely — SCA tooling, not a symbol graph server. NVD/OSV lookups, license checks. | PackageGraph data makes it easy for an agent to call a separate SCA tool. Not DocAgent's responsibility. |

---

## Feature Dependencies

```
packages.lock.json parsing (or project.assets.json fallback)
    └──provides──> PackageGraph (id, version, direct/transitive, dep links, assembly paths)
                       └──required by──> get_dependencies MCP tool
                       └──required by──> dependency path explanation
                       └──required by──> snapshot-level package version diff

DLL path resolution (NuGet cache layout + TFM best-match)
    └──required by──> Roslyn MetadataReference reflection
                           └──provides──> public API surface per assembly
                                              └──required by──> stub node enrichment
                                                                     └──required by──> find_package_usages MCP tool

PackageOrigin field on SymbolNode
    └──required by──> find_package_usages (fast package→stubs lookup)
    └──populated by──> stub node enrichment

Existing stub nodes (NodeKind.Stub, EdgeScope.External)
    └──already built, provide attachment points for──> stub enrichment

Existing PathAllowlist pattern
    └──replicated in──> PackageTools (new tool class)
```

### Dependency Notes

- `get_dependencies` can ship before DLL reflection is complete. PackageGraph is built from lock file alone, with no reflection needed.
- Stub enrichment requires DLL resolution first. You cannot call `MetadataReference.CreateFromFile` without the DLL path.
- `find_package_usages` requires enriched stubs with PackageOrigin. Without PackageOrigin, matching stubs to packages requires fragile assembly-name string matching.
- Lock file is optional at repo level. `packages.lock.json` is generated only when `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` is set. Fallback to `project.assets.json` (always present after restore in `obj/`) is required.
- TFM best-match is a prerequisite for reliable DLL resolution. Without it, packages targeting only `netstandard2.0` will not resolve on net10.0 projects.

---

## MVP Definition

### Launch With (v2.5)

All of the following are in scope per PROJECT.md.

- [ ] Parse `packages.lock.json` — direct + transitive dep tree, TFM-keyed — foundation for everything else
- [ ] Fallback to `project.assets.json` when lock file absent — required for repos that do not opt in to lock files
- [ ] `PackageGraph` domain type in DocAgent.Core — structured graph needed by both tools
- [ ] DLL path resolution from NuGet global packages cache with TFM best-match — required before reflection
- [ ] Roslyn MetadataReference reflection to extract public API surface per package — stub enrichment source
- [ ] `PackageOrigin` field on SymbolNode appended for MessagePack compat — fast package-to-symbol lookup
- [ ] Stub node enrichment pass (NodeKind.Stub nodes upgraded with real type info) — core value prop
- [ ] `get_dependencies` MCP tool — query the dependency graph
- [ ] `find_package_usages` MCP tool — cross-reference package exports with source references
- [ ] PathAllowlist security on PackageTools — consistent security posture

### Add After Validation (v2.5.x)

- [ ] Dependency path explanation ("why is X a dep") — add when agent feedback shows need to diagnose transitive dep chains
- [ ] Snapshot-level package version diff in `diff_solution_snapshots` — add when agents use change tracking across releases

### Future Consideration (v3+)

- [ ] npm/TypeScript package mapping — deferred per PROJECT.md; different parsing approach
- [ ] PackageReflectionCache as persistent sidecar — only if reflection performance becomes a problem at scale
- [ ] Doc coverage metrics for package-provided types — complexity high, user value unclear

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Lock file parsing + PackageGraph type | HIGH | LOW | P1 |
| project.assets.json fallback | HIGH | LOW | P1 |
| DLL path resolution + TFM best-match | HIGH | LOW-MEDIUM | P1 |
| Roslyn MetadataReference reflection | HIGH | MEDIUM | P1 |
| Stub node enrichment | HIGH | MEDIUM | P1 |
| PackageOrigin field on SymbolNode | MEDIUM | LOW | P1 |
| get_dependencies MCP tool | HIGH | LOW | P1 |
| find_package_usages MCP tool | HIGH | MEDIUM | P1 |
| PathAllowlist on PackageTools | HIGH | LOW | P1 |
| Dependency path explanation | MEDIUM | MEDIUM | P2 |
| Package version diff in snapshots | MEDIUM | MEDIUM | P2 |
| On-demand (lazy) reflection mode | MEDIUM | MEDIUM | P2 |
| contentHash verification vs DLL | LOW | LOW | P2 |

**Priority key:**
- P1: Must have for v2.5 launch
- P2: Should have, add when possible
- P3: Nice to have, future consideration

---

## Existing System Integration Points

Constraints and attach points from the already-built system that new features must respect.

| Existing Mechanism | How NuGet Mapping Attaches |
|--------------------|---------------------------|
| `NodeKind.Stub = 1` on SymbolNode | Stub nodes are already synthesized during solution ingestion for external type references via `SolutionIngestionService.MaybeAddStubNode`. Enrichment upgrades their payload in-place while preserving SymbolId. |
| `EdgeScope.External = 2` on SymbolEdge | External edges already connect source nodes to stub nodes. `find_package_usages` traverses these edges to find all references to a package's exported types. |
| `SymbolGraphSnapshot` (flat node + edge lists) | PackageGraph is NOT embedded in the snapshot. It lives alongside as a separate metadata file. Avoids inflating snapshot size and breaking determinism. |
| `SnapshotStore` (content-addressed, manifest.json) | PackageGraph stored as a companion file alongside each snapshot, keyed by the same content hash. |
| Enum append-only rule (MessagePack compat, from project MEMORY) | `PackageOrigin` field on SymbolNode must be appended at the end of the record. Any new enum values must also be appended. |
| BM25 stub filtering | BM25SearchIndex already filters NodeKind.Stub from search results. Enriched stubs remain filtered from search — agents query packages via `get_dependencies`, not free-text search. |
| PathAllowlist opaque not_found pattern | PackageTools must replicate the established pattern: reject out-of-allowlist paths with not_found, no error detail leakage. |
| Rate limiting (separate query/ingestion buckets) | `get_dependencies` and `find_package_usages` are query tools — use query bucket. Reflection during ingestion uses ingestion bucket. |
| Primitive type filter in stub synthesis | `SolutionIngestionService.s_primitiveTypeNames` already prevents stub nodes for System.Object etc. Enrichment should skip the same set. |

---

## Sources

- [NuGet lock file implementation spec (NuGet/Home Wiki)](https://github.com/NuGet/Home/wiki/Repeatable-build-using-lock-file-implementation)
- [Enable repeatable package restores using a lock file (.NET Blog)](https://devblogs.microsoft.com/dotnet/enable-repeatable-package-restores-using-a-lock-file/)
- [Managing NuGet global packages and cache folders (Microsoft Learn)](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)
- [MetadataReference Class — Roslyn (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.metadatareference?view=roslyn-dotnet-4.7.0)
- [Getting all INamedTypeSymbols in a Roslyn compilation (dotnet/roslyn issue #6138)](https://github.com/dotnet/roslyn/issues/6138)
- [dotnet nuget why command (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-why)
- [NuGet Package Dependency Resolution (Microsoft Learn)](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution)
- Existing codebase: `DocAgent.Core/Symbols.cs`, `SolutionTypes.cs`, `SolutionIngestionService.cs` (stub node synthesis pattern, NodeKind/EdgeScope design)
- `.planning/PROJECT.md` — v2.5 target feature list and out-of-scope constraints

---
*Feature research for: DocAgentFramework v2.5 NuGet Package Mapping milestone*
*Researched: 2026-03-26*
