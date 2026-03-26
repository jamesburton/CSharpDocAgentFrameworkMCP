# Requirements: DocAgentFramework

**Defined:** 2026-03-26
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET or TypeScript codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v2.5 Requirements

Requirements for NuGet Package Mapping milestone. Each maps to roadmap phases.

### Dependency Graph

- [ ] **DEP-01**: Parse `project.assets.json` to extract resolved direct + transitive dependency tree with package names, versions, and assembly mappings
- [ ] **DEP-02**: Parse `packages.lock.json` as secondary dependency source with v2 CPM format support and TFM key normalization
- [ ] **DEP-03**: Return structured diagnostic when neither dependency source file is found, suggesting `dotnet restore`
- [ ] **DEP-04**: `PackageGraph` domain type in `DocAgent.Core` with `PackageEntry`, `AssemblyMapping` records (MessagePack-compatible, immutable)

### Package Reflection

- [ ] **REFL-01**: Resolve NuGet package DLLs from global packages cache with cross-platform path resolution via `SettingsUtility.GetGlobalPackagesFolder()`
- [ ] **REFL-02**: TFM best-fit walk over `ref/` then `lib/` subfolders to find compatible DLL for `net10.0` projects
- [ ] **REFL-03**: Extract public types and their public members (methods, properties, fields, events) via Roslyn `AssemblyMetadata` reflection
- [ ] **REFL-04**: Bounded `AssemblyMetadata` cache keyed on `(packageId, version, tfm)` with ~200 entry cap and IDisposable cleanup
- [ ] **REFL-05**: Validate all derived DLL paths are under NuGet cache root before loading — no arbitrary DLL loading

### Symbol Graph Integration

- [ ] **INTG-01**: Enrich existing stub nodes with real type info from DLL reflection (`NodeKind.Stub` → `NodeKind.Enriched`)
- [ ] **INTG-02**: `PackageOrigin` field on `SymbolNode` populated during enrichment (appended for MessagePack compat)
- [ ] **INTG-03**: Stub-to-reflected-type matching via `(AssemblyName, MetadataName)` tuples — not display strings
- [ ] **INTG-04**: BM25 search index includes `NodeKind.Enriched` nodes alongside `NodeKind.Real`
- [ ] **INTG-05**: `PackageQueryService` in-memory cache of `PackageGraph` per snapshot, populated by ingestion

### MCP Tools

- [ ] **TOOL-01**: `get_dependencies` MCP tool — query package dependency graph for a project with json/markdown/tron output
- [ ] **TOOL-02**: `find_package_usages` MCP tool — find source references to a package's exported types with json/markdown/tron output
- [ ] **TOOL-03**: `explain_dependency_path` MCP tool — BFS over `PackageGraph` edges explaining why a package is a transitive dependency
- [ ] **TOOL-04**: PathAllowlist security enforcement on all PackageTools methods

## Future Requirements

Deferred to future release. Tracked but not in current roadmap.

### npm/TypeScript Package Mapping

- **NPM-01**: Parse `package.json` dependencies for TypeScript projects
- **NPM-02**: Resolve `node_modules` type declarations for public API extraction
- **NPM-03**: Link npm package exports to TypeScript symbol graph references

### Package Intelligence

- **PKGI-01**: Package version diff in `diff_solution_snapshots` — track dependency changes across snapshots
- **PKGI-02**: Persistent `PackageReflectionCache` sidecar for reflection performance at scale

## Out of Scope

| Feature | Reason |
|---------|--------|
| Downloading packages from nuget.org at runtime | Violates "no implicit network calls" constraint; breaks air-gapped environments |
| Eager full transitive DLL reflection at ingestion | Reflecting 200+ packages per solution makes ingestion too slow |
| `enrich_stubs` / `reflect_packages` as user-facing MCP tools | Enrichment is an internal optimization pass, not a user operation |
| Cross-language package graphs (NuGet ↔ npm) | Separate ecosystems; no integration needed for v2.5 |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| DEP-01 | Phase 36 | Pending |
| DEP-02 | Phase 36 | Pending |
| DEP-03 | Phase 36 | Pending |
| DEP-04 | Phase 36 | Pending |
| REFL-01 | Phase 37 | Pending |
| REFL-02 | Phase 37 | Pending |
| REFL-03 | Phase 37 | Pending |
| REFL-04 | Phase 37 | Pending |
| REFL-05 | Phase 37 | Pending |
| INTG-01 | Phase 38 | Pending |
| INTG-02 | Phase 38 | Pending |
| INTG-03 | Phase 38 | Pending |
| INTG-04 | Phase 38 | Pending |
| INTG-05 | Phase 39 | Pending |
| TOOL-01 | Phase 40 | Pending |
| TOOL-02 | Phase 40 | Pending |
| TOOL-03 | Phase 40 | Pending |
| TOOL-04 | Phase 40 | Pending |

**Coverage:**
- v2.5 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0

---
*Requirements defined: 2026-03-26*
*Last updated: 2026-03-26 after roadmap creation — all 18 requirements mapped*
