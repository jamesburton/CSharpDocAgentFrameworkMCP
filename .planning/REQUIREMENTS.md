# Requirements: DocAgentFramework

**Defined:** 2026-03-01
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v1.2 Requirements

Requirements for multi-project / solution-level graph support. Each maps to roadmap phases.

### Solution Ingestion

- [x] **INGEST-01**: Agent can ingest an entire .sln file in one call via `ingest_solution` MCP tool
- [x] **INGEST-02**: Non-C# projects in a solution are skipped gracefully with logged warnings
- [x] **INGEST-03**: Multi-targeting projects (e.g. net10.0;net48) are deduplicated to a single TFM
- [x] **INGEST-04**: MSBuildWorkspace load failures are detected and reported (WorkspaceFailed handler, document count validation)
- [ ] **INGEST-05**: Per-project incremental re-ingestion within a solution — change one project, re-ingest only that project
- [x] **INGEST-06**: `ingest_solution` is secured with PathAllowlist enforcement (consistent with existing tool security pattern)

### Solution Graph Model

- [x] **GRAPH-01**: `SolutionSnapshot` aggregate type holds per-project `SymbolGraphSnapshot`s with solution-level metadata
- [x] **GRAPH-02**: Cross-project `SymbolEdge`s link symbols across project boundaries (inherits, implements, calls, references)
- [x] **GRAPH-03**: Project dependency DAG is first-class data in `SolutionSnapshot` (ProjectEdge collection)
- [x] **GRAPH-04**: Stub/metadata nodes for NuGet package types (type name, namespace, member signatures; flagged `IsExternal`)
- [x] **GRAPH-05**: Stub nodes are filtered at index time to prevent BM25 search pollution (NodeKind discriminator)
- [x] **GRAPH-06**: New fields on existing types use nullable/default values for backward compatibility with v1.0/v1.1 snapshots

### Solution-Aware Tools

- [x] **TOOLS-01**: `search_symbols` returns results from all projects in a solution
- [x] **TOOLS-02**: `get_symbol` resolves by fully qualified name across any project in the solution
- [x] **TOOLS-03**: `get_references` spans project boundaries for cross-project "who calls this?" queries
- [ ] **TOOLS-04**: `diff_snapshots` works at solution level (diff two SolutionSnapshots)
- [x] **TOOLS-05**: New `explain_solution` MCP tool provides solution-level architecture overview (project list, dependency DAG, node/edge counts, doc coverage per project)
- [x] **TOOLS-06**: Existing tools accept optional `project` filter parameter to scope results to a single project

## Future Requirements

Deferred to later milestones. Tracked but not in current roadmap.

### Package Mapping

- **PKG-01**: Parse .csproj PackageReference items into PackageRefGraph
- **PKG-02**: Parse lock files for transitive dependency resolution
- **PKG-03**: Map nuspec metadata to package nodes

### Advanced Indexing

- **IDX-01**: Embeddings/vector index for semantic search
- **IDX-02**: Query DSL over symbol graph

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Full NuGet package source ingestion | Index bloats to gigabytes; stub nodes sufficient for v1.2 |
| One merged flat SymbolGraphSnapshot | Breaks per-project identity and determinism contract |
| Real-time file-watch re-ingestion | MCP model is request-driven; explicit triggers preferred |
| Cross-language graph (C# + F#) | Polyglot deferred to future tier per project plan |
| Full interprocedural call graph | Expensive at scale; cross-project reference edges sufficient |
| Non-stdio MCP transports | Deferred to later |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| INGEST-01 | Phase 14 | Complete |
| INGEST-02 | Phase 14 | Complete |
| INGEST-03 | Phase 14 | Complete |
| INGEST-04 | Phase 14 | Complete |
| INGEST-05 | Phase 17 | Pending |
| INGEST-06 | Phase 14 | Complete |
| GRAPH-01 | Phase 13 (types) + Phase 14.1 (populate) | Partial — types defined, population pending |
| GRAPH-02 | Phase 13 (types) + Phase 14.1 (classify) | Partial — EdgeScope defined, classification pending |
| GRAPH-03 | Phase 13 (types) + Phase 14.1 (populate) | Partial — ProjectEdge defined, DAG population pending |
| GRAPH-04 | Phase 13 (types) + Phase 14.1 (synthesize) | Partial — NodeKind defined, stub synthesis pending |
| GRAPH-05 | Phase 13 (types) + Phase 15 (filter) | Partial — projectFilter param added, filtering pending |
| GRAPH-06 | Phase 13 | Complete |
| TOOLS-01 | Phase 15 | Complete |
| TOOLS-02 | Phase 15 | Complete |
| TOOLS-03 | Phase 15 | Complete |
| TOOLS-04 | Phase 16 | Pending |
| TOOLS-05 | Phase 16 | Complete |
| TOOLS-06 | Phase 15 | Complete |

**Coverage:**
- v1.2 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-01*
*Last updated: 2026-03-01 after roadmap creation (phases 13-17 assigned)*
