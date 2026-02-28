# Requirements: DocAgentFramework

**Defined:** 2026-02-26
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Core Domain

- [x] **CORE-01**: Stable `SymbolId` spec with assembly-qualified identity and rename tracking (`PreviousIds`)
- [x] **CORE-02**: `SymbolGraphSnapshot` schema with version field, content hash, and deterministic serialization
- [x] **CORE-03**: Domain interfaces: `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `ISearchIndex`, `IKnowledgeQueryService`

### Ingestion

- [x] **INGS-01**: Roslyn symbol graph walker — namespaces, types, members with file spans and parent/child relationships
- [x] **INGS-02**: XML doc parser with proper symbol binding (summary, param, returns, remarks, exceptions)
- [x] **INGS-03**: Handle XML doc edge cases: generics, partial types, overloads, operators, `inheritdoc` expansion
- [x] **INGS-04**: `SnapshotStore` — write/read versioned snapshots to `artifacts/snapshots/`
- [x] **INGS-05**: Determinism test: same input produces byte-identical `SymbolGraphSnapshot` across runs
- [x] **INGS-06**: Runtime ingestion trigger — MCP tool to invoke full pipeline (discover → parse → snapshot → index) at runtime

### Indexing

- [x] **INDX-01**: BM25 search index over symbol names and doc text replacing `InMemorySearchIndex`
- [x] **INDX-02**: CamelCase-aware tokenization for symbol name search
- [x] **INDX-03**: Index persistence alongside snapshots

### Query

- [x] **QURY-01**: `IKnowledgeQueryService` facade wired to `ISearchIndex` + `SnapshotStore`
- [x] **QURY-02**: `SearchAsync` — ranked symbol search results
- [x] **QURY-03**: `GetSymbolAsync` — full symbol detail by ID
- [x] **QURY-04**: `DiffAsync` — basic structural diff between snapshots

### MCP Serving

- [x] **MCPS-01**: `search_symbols` MCP tool via stdio transport
- [x] **MCPS-02**: `get_symbol` MCP tool
- [x] **MCPS-03**: `get_references` MCP tool
- [x] **MCPS-04**: `diff_snapshots` MCP tool
- [x] **MCPS-05**: `explain_project` MCP tool
- [x] **MCPS-06**: Stderr-only logging (no stdout contamination of MCP JSON-RPC framing)

### Security

- [x] **SECR-01**: Path allowlist — default-deny, only allowed directories accessible
- [x] **SECR-02**: Audit logging — log every tool call with input/output
- [x] **SECR-03**: Input validation — defense against prompt injection via structured DTOs

### Analysis

- [x] **ANLY-01**: Roslyn analyzer: detect public API changes not reflected in documentation
- [x] **ANLY-02**: Roslyn analyzer: detect suspicious edits (semantic changes without doc/test updates)
- [x] **ANLY-03**: Doc coverage policy enforcement for public symbols (configurable threshold)

### Hosting

- [x] **HOST-01**: Aspire app host with DI extension methods (`AddDocAgentCore`, etc.)
- [x] **HOST-02**: OpenTelemetry wiring for tool call observation

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Diff & Review

- **DIFF-01**: Symbol-level semantic diff engine with risk scoring (nullability, accessibility, constraints)
- **DIFF-02**: `review_changes` MCP tool (Unusual Change Review skill)

### Package Analysis

- **PKGR-01**: PackageRefGraph — parse `.csproj`, `Directory.Packages.props`, `packages.lock.json`

### Search

- **VCTR-01**: `IVectorIndex` implementation (embeddings-based semantic search)

### Transport

- **TRNS-01**: HTTP/SSE MCP transport with auth model

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Polyglot support (Tree-sitter, Python, TypeScript) | Dilutes compiler-accuracy thesis; lock to C#/Roslyn until generic builder is stable |
| Query DSL (CodeQL-like) | Premature without known query patterns from real agent usage |
| Structural code rewrite / auto-fix engine | Trust problem — conflating read and write paths multiplies attack surface |
| IDE plugin distribution (VS, VS Code, Rider) | Marketplace approval and version compat matrices; prove value via agent/CI first |
| Real-time file-watch / incremental re-indexing | Significant engineering problem; rebuild-on-demand is fast enough for V1 |
| Multi-tenant authorization | Requires non-stdio transport and stable data model; V1 is single-tenant local |
| Embeddings in search (V1) | Non-determinism, provider coupling; BM25 covers 80% of navigation needs |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| CORE-01 | Phase 1 | Complete |
| CORE-02 | Phase 1 | Complete |
| CORE-03 | Phase 1 | Complete |
| INGS-01 | Phase 2 | Complete |
| INGS-02 | Phase 2 | Complete |
| INGS-03 | Phase 2 | Complete |
| INGS-04 | Phase 2 | Complete |
| INGS-05 | Phase 2 | Complete |
| INDX-01 | Phase 3 | Complete |
| INDX-02 | Phase 3 | Complete |
| INDX-03 | Phase 3 | Complete |
| QURY-01 | Phase 4 | Complete |
| QURY-02 | Phase 4 | Complete |
| QURY-03 | Phase 4 | Complete |
| QURY-04 | Phase 4 | Complete |
| MCPS-01 | Phase 5 | Complete |
| MCPS-02 | Phase 5 | Complete |
| MCPS-03 | Phase 5 | Complete |
| MCPS-04 | Phase 5 | Complete |
| MCPS-05 | Phase 5 | Complete |
| MCPS-06 | Phase 5 | Complete |
| SECR-01 | Phase 5 | Complete |
| SECR-02 | Phase 5 | Complete |
| SECR-03 | Phase 5 | Complete |
| ANLY-01 | Phase 6 | Complete |
| ANLY-02 | Phase 6 | Complete |
| ANLY-03 | Phase 6 | Complete |
| HOST-01 | Phase 6 | Complete |
| HOST-02 | Phase 6 | Complete |
| INGS-06 | Phase 8 | Complete |

**Coverage:**
- v1 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0 ✓

---
*Requirements defined: 2026-02-26*
*Last updated: 2026-02-26 after roadmap creation — all 29 requirements mapped*
