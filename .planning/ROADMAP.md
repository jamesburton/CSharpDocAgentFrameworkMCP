# Roadmap: DocAgentFramework

## Milestones

- ✅ **v1.0 MVP** — Phases 1-8 (shipped 2026-02-28)
- ✅ **v1.1 Semantic Diff & Change Intelligence** — Phases 9-12 (shipped 2026-03-01)
- ✅ **v1.2 Multi-Project & Solution-Level Graphs** — Phases 13-18 (shipped 2026-03-02)
- ✅ **v1.3 Housekeeping** — Phases 19-22 (shipped 2026-03-04)
- ✅ **v1.5 Robustness** — Phases 23-27 (shipped 2026-03-08)
- ✅ **v2.0 TypeScript Language Support** — Phases 28-35 (shipped 2026-03-26)
- 🚧 **v2.5 NuGet Package Mapping** — Phases 36-40 (in progress)

## Phases

<details>
<summary>v1.0 MVP (Phases 1-8) -- SHIPPED 2026-02-28</summary>

- [x] Phase 1: Core Domain (3/3 plans) -- completed 2026-02-26
- [x] Phase 2: Ingestion Pipeline (5/5 plans) -- completed 2026-02-26
- [x] Phase 3: BM25 Search Index (2/2 plans) -- completed 2026-02-26
- [x] Phase 4: Query Facade (2/2 plans) -- completed 2026-02-26
- [x] Phase 5: MCP Server + Security (3/3 plans) -- completed 2026-02-27
- [x] Phase 7: Runtime Integration Wiring (3/3 plans) -- completed 2026-02-27
- [x] Phase 6: Analysis + Hosting (4/4 plans) -- completed 2026-02-28
- [x] Phase 8: Ingestion Runtime Trigger (2/2 plans) -- completed 2026-02-28

Full details: milestones/v1.0-ROADMAP.md

</details>

<details>
<summary>v1.1 Semantic Diff & Change Intelligence (Phases 9-12) -- SHIPPED 2026-03-01</summary>

- [x] Phase 9: Semantic Diff Engine (3/3 plans) -- completed 2026-02-28
- [x] Phase 10: Incremental Ingestion (3/3 plans) -- completed 2026-02-28
- [x] Phase 11: Change Intelligence & Review (2/2 plans) -- completed 2026-02-28
- [x] Phase 12: ChangeTools Security Gate (1/1 plan) -- completed 2026-03-01

Full details: milestones/v1.1-ROADMAP.md

</details>

<details>
<summary>v1.2 Multi-Project & Solution-Level Graphs (Phases 13-18) -- SHIPPED 2026-03-02</summary>

- [x] Phase 13: Core Domain Extensions (2/2 plans) -- completed 2026-03-01
- [x] Phase 14: Solution Ingestion Pipeline (2/2 plans) -- completed 2026-03-01
- [x] Phase 14.1: Solution Graph Enrichment (2/2 plans) -- completed 2026-03-01
- [x] Phase 15: Project-Aware Indexing & Query (2/2 plans) -- completed 2026-03-01
- [x] Phase 16: Solution MCP Tools (2/2 plans) -- completed 2026-03-02
- [x] Phase 18: Fix diff_snapshots Tool Name Collision (1/1 plan) -- completed 2026-03-02

Full details: milestones/v1.2-ROADMAP.md

Known gap: Phase 17 (Incremental Solution Re-ingestion / INGEST-05) deferred to v1.3

</details>

<details>
<summary>v1.3 Housekeeping (Phases 19-22) -- SHIPPED 2026-03-04</summary>

- [x] Phase 19: Incremental Solution Re-ingestion (4/4 plans) -- completed 2026-03-02
- [x] Phase 20: MSBuild Performance Benchmarks (2/2 plans) -- completed 2026-03-03
- [x] Phase 21: Code and Audit Cleanup (1/1 plan) -- completed 2026-03-03
- [x] Phase 22: Documentation Refresh (1/1 plan) -- completed 2026-03-04

Full details: milestones/v1.3-ROADMAP.md

</details>

<details>
<summary>v1.5 Robustness (Phases 23-27) -- SHIPPED 2026-03-08</summary>

- [x] Phase 23: Dependency Foundation (1/1 plan) -- completed 2026-03-06
- [x] Phase 24: Query Performance (1/1 plan) -- completed 2026-03-08
- [x] Phase 25: Server Infrastructure (2/2 plans) -- completed 2026-03-08
- [x] Phase 26: API Extensions (2/2 plans) -- completed 2026-03-08
- [x] Phase 27: Documentation Refresh (1/1 plan) -- completed 2026-03-08

Full details: milestones/v1.5-ROADMAP.md

</details>

<details>
<summary>v2.0 TypeScript Language Support (Phases 28-35) -- SHIPPED 2026-03-26</summary>

- [x] Phase 28: Sidecar Scaffold and IPC Protocol (2/2 plans) -- completed 2026-03-08
- [x] Phase 29: Core Symbol Extraction (3/3 plans) -- completed 2026-03-24
- [x] Phase 30: MCP Integration and Incremental Ingestion (3/3 plans) -- completed 2026-03-25
- [x] Phase 31: Verification and Hardening (4/4 plans) -- completed 2026-03-25
- [x] Phase 32: JSON Contract Alignment (2/2 plans) -- completed 2026-03-26
- [x] Phase 33: Aspire Sidecar Integration (1/1 plan) -- completed 2026-03-26
- [x] Phase 34: Traceability and Verification Cleanup (2/2 plans) -- completed 2026-03-26
- [x] Phase 35: Contract Fidelity and CI Observability (2/2 plans) -- completed 2026-03-26

Full details: milestones/v2.0-ROADMAP.md

</details>

### 🚧 v2.5 NuGet Package Mapping (In Progress)

**Milestone Goal:** Agents can query the full NuGet dependency graph and cross-reference package-exported public APIs with indexed source code references.

- [ ] **Phase 36: Domain Types + Dependency Source Parsing** - `PackageGraph` core types in DocAgent.Core and dual-source dependency parsing (`project.assets.json` primary, `packages.lock.json` secondary)
- [ ] **Phase 37: DLL Resolution + AssemblyMetadata Cache** - NuGet cache path resolution, TFM best-fit DLL walk, bounded AssemblyMetadata cache, DLL path security validation
- [ ] **Phase 38: Stub Enrichment + BM25 Index Update** - Stub-to-reflected-type matching via `(AssemblyName, MetadataName)` tuples, `NodeKind.Enriched` promotion, `PackageOrigin` field population, BM25 filter update
- [ ] **Phase 39: Pipeline Integration + Service Wiring** - `SolutionIngestionService` enrichment pipeline integration, `PackageQueryService` in-memory cache, DI registration
- [ ] **Phase 40: MCP Tools + Security** - `get_dependencies`, `find_package_usages`, `explain_dependency_path` tools with PathAllowlist enforcement and CLAUDE.md update

## Phase Details

### Phase 36: Domain Types + Dependency Source Parsing
**Goal**: The PackageGraph domain type and dual-source dependency parser exist and correctly represent a project's resolved NuGet dependency tree
**Depends on**: Phase 35
**Requirements**: DEP-01, DEP-02, DEP-03, DEP-04
**Success Criteria** (what must be TRUE):
  1. A unit test can parse this project's `obj/project.assets.json` and receive a `PackageGraph` with correct direct + transitive entries and assembly mappings
  2. A unit test can parse a real `packages.lock.json` (v2 CPM format) and receive the same package versions with correct TFM key normalization (`.NETCoreApp,Version=v10.0` not `net10.0`)
  3. When neither dependency file is present, the parser returns a structured diagnostic message — not an exception — that includes a `dotnet restore` suggestion
  4. `PackageEntry`, `PackageGraph`, and `AssemblyMapping` records round-trip through MessagePack without data loss
  5. `NodeKind.Enriched = 2` is appended to the `NodeKind` enum and existing artifacts with `NodeKind.Real` and `NodeKind.Stub` deserialize correctly
**Plans**: TBD

### Phase 37: DLL Resolution + AssemblyMetadata Cache
**Goal**: Given a package name, version, and target framework, the NuGet cache reflector can locate the correct DLL, extract its public API surface, and cache the result without native heap growth
**Depends on**: Phase 36
**Requirements**: REFL-01, REFL-02, REFL-03, REFL-04, REFL-05
**Success Criteria** (what must be TRUE):
  1. The reflector resolves the NuGet global packages folder via `SettingsUtility.GetGlobalPackagesFolder()` and correctly finds a `netstandard2.0`-only package (e.g., `YamlDotNet`) as compatible with `net10.0` via the TFM best-fit walk
  2. Public types and their public members (methods, properties, fields, events) are extracted from a known package DLL and match the package's actual public API surface
  3. Ten consecutive ingestion cycles produce stable process RSS after the first warm-up cycle, confirming the `AssemblyMetadata` bounded cache prevents heap growth
  4. Any DLL path derived from a lock/assets file that is not under the NuGet cache root is rejected before `CreateFromFile` is called; internal types are filtered from reflected output
  5. When a DLL cannot be located for a package, the reflector appends a warning to the accumulator and returns without throwing, allowing ingestion to proceed with partial data
**Plans**: TBD

### Phase 38: Stub Enrichment + BM25 Index Update
**Goal**: Existing stub nodes in solution snapshots are upgraded to enriched nodes with real type info and PackageOrigin, and enriched nodes are discoverable via BM25 search
**Depends on**: Phase 37
**Requirements**: INTG-01, INTG-02, INTG-03, INTG-04
**Success Criteria** (what must be TRUE):
  1. Stub nodes for generic types (`IEnumerable<T>`, `Task<TResult>`, `IReadOnlyDictionary<TKey, TValue>`) produced by the existing `SolutionIngestionService` are correctly matched and promoted to `NodeKind.Enriched` via `(AssemblyName, MetadataName)` tuple matching
  2. Enriched nodes carry a populated `PackageOrigin` field that identifies the source package name and version
  3. A `search_symbols` query for a type name from a known NuGet package returns the enriched node in results (confirming BM25 includes `NodeKind.Enriched`)
  4. Development-only packages (marked `PrivateAssets="All"` in assets file) are not reflected and do not appear as enriched nodes in the symbol graph
**Plans**: TBD

### Phase 39: Pipeline Integration + Service Wiring
**Goal**: The enrichment pipeline runs automatically after solution ingestion, and the resulting PackageGraph is accessible in PackageQueryService without additional user action
**Depends on**: Phase 38
**Requirements**: INTG-05
**Success Criteria** (what must be TRUE):
  1. After calling `ingest_solution`, the `PackageQueryService` holds a `PackageGraph` for each ingested project without any additional user action
  2. The `SolutionIngestionService` integration tests pass without accessing the real NuGet cache, using `PipelineOverride` seams for the lock file parser and reflector
  3. Re-ingesting the same solution replaces the cached `PackageGraph` with a fresh one rather than serving stale data
**Plans**: TBD

### Phase 40: MCP Tools + Security
**Goal**: Agents can call get_dependencies, find_package_usages, and explain_dependency_path to query the NuGet dependency graph, with full PathAllowlist security enforcement
**Depends on**: Phase 39
**Requirements**: TOOL-01, TOOL-02, TOOL-03, TOOL-04
**Success Criteria** (what must be TRUE):
  1. `get_dependencies` returns the full dependency tree for a project in json, markdown, and tron formats, matching the response structure of existing tools
  2. `find_package_usages` returns source symbols that reference types from a named package, backed by enriched stub nodes with `PackageOrigin`
  3. `explain_dependency_path` returns a human-readable BFS explanation of why a named transitive package is in the dependency graph
  4. Calling any PackageTool with a path outside the configured PathAllowlist returns an opaque not-found denial — no path information leaked
  5. CLAUDE.md is updated with all three new tools documented including parameter signatures verified against source
**Plans**: TBD

## Progress

**Execution Order:** 36 → 37 → 38 → 39 → 40

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Core Domain | v1.0 | 3/3 | Complete | 2026-02-26 |
| 2. Ingestion Pipeline | v1.0 | 5/5 | Complete | 2026-02-26 |
| 3. BM25 Search Index | v1.0 | 2/2 | Complete | 2026-02-26 |
| 4. Query Facade | v1.0 | 2/2 | Complete | 2026-02-26 |
| 5. MCP Server + Security | v1.0 | 3/3 | Complete | 2026-02-27 |
| 7. Runtime Integration Wiring | v1.0 | 3/3 | Complete | 2026-02-27 |
| 6. Analysis + Hosting | v1.0 | 4/4 | Complete | 2026-02-28 |
| 8. Ingestion Runtime Trigger | v1.0 | 2/2 | Complete | 2026-02-28 |
| 9. Semantic Diff Engine | v1.1 | 3/3 | Complete | 2026-02-28 |
| 10. Incremental Ingestion | v1.1 | 3/3 | Complete | 2026-02-28 |
| 11. Change Intelligence & Review | v1.1 | 2/2 | Complete | 2026-02-28 |
| 12. ChangeTools Security Gate | v1.1 | 1/1 | Complete | 2026-03-01 |
| 13. Core Domain Extensions | v1.2 | 2/2 | Complete | 2026-03-01 |
| 14. Solution Ingestion Pipeline | v1.2 | 2/2 | Complete | 2026-03-01 |
| 14.1 Solution Graph Enrichment | v1.2 | 2/2 | Complete | 2026-03-01 |
| 15. Project-Aware Indexing & Query | v1.2 | 2/2 | Complete | 2026-03-01 |
| 16. Solution MCP Tools | v1.2 | 2/2 | Complete | 2026-03-02 |
| 18. Fix diff_snapshots Collision | v1.2 | 1/1 | Complete | 2026-03-02 |
| 19. Incremental Solution Re-ingestion | v1.3 | 4/4 | Complete | 2026-03-02 |
| 20. MSBuild Performance Benchmarks | v1.3 | 2/2 | Complete | 2026-03-03 |
| 21. Code and Audit Cleanup | v1.3 | 1/1 | Complete | 2026-03-03 |
| 22. Documentation Refresh | v1.3 | 1/1 | Complete | 2026-03-04 |
| 23. Dependency Foundation | v1.5 | 1/1 | Complete | 2026-03-06 |
| 24. Query Performance | v1.5 | 1/1 | Complete | 2026-03-08 |
| 25. Server Infrastructure | v1.5 | 2/2 | Complete | 2026-03-08 |
| 26. API Extensions | v1.5 | 2/2 | Complete | 2026-03-08 |
| 27. Documentation Refresh | v1.5 | 1/1 | Complete | 2026-03-08 |
| 28. Sidecar Scaffold and IPC Protocol | v2.0 | 2/2 | Complete | 2026-03-08 |
| 29. Core Symbol Extraction | v2.0 | 3/3 | Complete | 2026-03-24 |
| 30. MCP Integration and Incremental Ingestion | v2.0 | 3/3 | Complete | 2026-03-25 |
| 31. Verification and Hardening | v2.0 | 4/4 | Complete | 2026-03-25 |
| 32. JSON Contract Alignment | v2.0 | 2/2 | Complete | 2026-03-26 |
| 33. Aspire Sidecar Integration | v2.0 | 1/1 | Complete | 2026-03-26 |
| 34. Traceability and Verification Cleanup | v2.0 | 2/2 | Complete | 2026-03-26 |
| 35. Contract Fidelity and CI Observability | v2.0 | 2/2 | Complete | 2026-03-26 |
| 36. Domain Types + Dep Parsing | v2.5 | 0/TBD | Not started | - |
| 37. DLL Resolution + Cache | v2.5 | 0/TBD | Not started | - |
| 38. Stub Enrichment + BM25 | v2.5 | 0/TBD | Not started | - |
| 39. Pipeline Integration | v2.5 | 0/TBD | Not started | - |
| 40. MCP Tools + Security | v2.5 | 0/TBD | Not started | - |
