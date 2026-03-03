# Requirements: DocAgentFramework

**Defined:** 2026-03-02
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v1.3 Requirements

Requirements for v1.3 Housekeeping milestone. Each maps to roadmap phases.

### Incremental Ingestion

- [x] **INGEST-01**: Solution re-ingestion skips unchanged projects (per-project SHA-256 manifest comparison)
- [x] **INGEST-02**: Dependency cascade marks downstream projects dirty when their dependencies change
- [x] **INGEST-03**: Per-project manifests use path-based keys to prevent collision
- [x] **INGEST-04**: Stub nodes from prior ingestions are correctly regenerated, not accumulated
- [x] **INGEST-05**: Incremental solution result is byte-identical to full re-ingestion for unchanged input

### Performance

- [x] **PERF-01**: MSBuild workspace open and per-project compilation latency is measured and baselined
- [x] **PERF-02**: Memory high-water mark during solution ingestion is measured and baselined
- [x] **PERF-03**: Regression guard test asserts ingestion stays under defined thresholds

### Code Quality

- [ ] **QUAL-01**: Stale "TODO: replace with BM25" comment removed from InMemorySearchIndex.cs
- [ ] **QUAL-02**: Stale "stub" comment removed from KnowledgeQueryService.cs:215
- [ ] **QUAL-03**: v1.2 audit artifact issues resolved (stale frontmatter, documentation gaps)

### Documentation

- [ ] **DOCS-01**: Architecture.md reflects current 6-project structure and 12 MCP tools
- [ ] **DOCS-02**: Plan.md updated to reflect v1.0-v1.2 shipped reality
- [ ] **DOCS-03**: Testing.md updated with current test count and strategy

## Future Requirements

Deferred to future milestones. Tracked but not in v1.3 roadmap.

### Package Mapping (v1.5)

- **PKG-01**: Parse csproj, lock files, nuspec into PackageRefGraph
- **PKG-02**: Surface package dependencies via MCP tools

### Embeddings (TBD)

- **EMB-01**: Implement IVectorIndex with chosen embeddings provider

## Out of Scope

| Feature | Reason |
|---------|--------|
| Non-stdio MCP transports (HTTP, SSE) | No demand yet; security model not designed |
| Polyglot support (Tree-sitter, LSP) | Future tier; .NET-only for now |
| Source generators | V3 deliverable |
| Query DSL over symbol graph | Speculative/long-term |
| Parallel project ingestion in solution | MSBuildWorkspace is not thread-safe |
| Skipping MSBuildWorkspace open for unchanged solutions | Requires DAG cache; out of scope for v1.3 |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| INGEST-01 | Phase 19 | Complete |
| INGEST-02 | Phase 19 | Complete |
| INGEST-03 | Phase 19 | Complete |
| INGEST-04 | Phase 19 | Complete |
| INGEST-05 | Phase 19 | Complete |
| PERF-01 | Phase 20 | Complete |
| PERF-02 | Phase 20 | Complete |
| PERF-03 | Phase 20 | Complete |
| QUAL-01 | Phase 21 | Pending |
| QUAL-02 | Phase 21 | Pending |
| QUAL-03 | Phase 21 | Pending |
| DOCS-01 | Phase 22 | Pending |
| DOCS-02 | Phase 22 | Pending |
| DOCS-03 | Phase 22 | Pending |

**Coverage:**
- v1.3 requirements: 14 total
- Mapped to phases: 14
- Unmapped: 0

---
*Requirements defined: 2026-03-02*
*Last updated: 2026-03-02 after initial definition*
