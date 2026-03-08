# Requirements: DocAgentFramework

**Defined:** 2026-03-04
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v1.5 Requirements

Requirements for v1.5 Robustness milestone. Each maps to roadmap phases.

### Operational Polish

- [ ] **OPS-01**: CLAUDE.md lists all MCP tools (currently 12, becoming 14) with parameters, format options, and projectFilter
- [x] **OPS-02**: Server validates configuration on startup (AllowedPaths non-empty, ArtifactsDir writable) and logs diagnostic summary
- [x] **OPS-03**: Rate limiter throttles tool invocations per configurable token-bucket policy, returning structured error on limit exceeded

### Package Maintenance

- [x] **PKG-01**: All five Microsoft.CodeAnalysis packages upgraded to 4.14.0, VersionOverride workaround removed
- [x] **PKG-02**: All NuGet dependencies audited for vulnerabilities and outdated versions; NuGetAudit enabled in Directory.Build.props

### API Completeness

- [x] **API-01**: get_references supports offset/limit pagination with total count in response envelope
- [x] **API-02**: find_implementations tool returns all types implementing a given interface or deriving from a base class, with stub node filtering
- [x] **API-03**: get_doc_coverage tool returns documentation coverage metrics grouped by project, namespace, and symbol kind

### Query Performance

- [x] **PERF-01**: Symbol existence check in KnowledgeQueryService uses O(1) HashSet/Dictionary lookup instead of O(n) linear scan
- [x] **PERF-02**: Edge traversal in GetSymbolAsync/GetReferencesAsync uses pre-built edge-by-source/edge-by-target dictionaries instead of O(E) scan
- [x] **PERF-03**: SearchAsync metadata retrieval uses cached node data instead of per-hit async I/O

## Future Requirements

Deferred to future milestones. Tracked but not in v1.5 roadmap.

### Package Mapping (v2.0)

- **PKG-MAP-01**: Parse csproj, lock files, nuspec into PackageRefGraph
- **PKG-MAP-02**: Surface package dependencies via MCP tools

### Embeddings (TBD)

- **EMB-01**: Implement IVectorIndex with chosen embeddings provider

### Advanced Queries (TBD)

- **QUERY-01**: Call hierarchy tool (multi-level caller chains)
- **QUERY-02**: Regex/fuzzy symbol pattern matching tool
- **QUERY-03**: Graph export to external formats (Neo4j, DOT)

## Out of Scope

| Feature | Reason |
|---------|--------|
| Non-stdio MCP transports (HTTP, SSE) | No demand yet; security model not designed |
| Polyglot support (Tree-sitter, LSP) | Future tier; .NET-only for now |
| Source generators | V3 deliverable |
| Query DSL over symbol graph | Speculative/long-term |
| Live Roslyn SymbolFinder queries | v1.5 uses snapshot edges; live queries deferred |
| Streaming MCP responses | MCP spec doesn't support streaming tool responses |
| Per-client identity/auth | Stdio is single-client; auth not meaningful |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| PKG-01 | Phase 23 | Complete |
| PKG-02 | Phase 23 | Complete |
| PERF-01 | Phase 24 | Complete |
| PERF-02 | Phase 24 | Complete |
| PERF-03 | Phase 24 | Complete |
| OPS-02 | Phase 25 | Complete |
| OPS-03 | Phase 25 | Complete |
| API-01 | Phase 26 | Complete |
| API-02 | Phase 26 | Complete |
| API-03 | Phase 26 | Complete |
| OPS-01 | Phase 27 | Pending |

**Coverage:**
- v1.5 requirements: 11 total
- Mapped to phases: 11
- Unmapped: 0

---
*Requirements defined: 2026-03-04*
*Last updated: 2026-03-08 — API-01/02/03 complete (Phase 26), PERF-01/02/03 complete (Phase 24), PKG-01/02 complete (Phase 23)*
