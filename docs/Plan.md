# DocAgentFramework — Implementation History and Roadmap

A .NET MCP server that ingests code documentation and Roslyn symbol data, serving it as a queryable symbol graph via MCP tools.

---

## Shipped Milestones

### v1.0 — MVP (shipped 2026-02-28)

Core pipeline from ingestion to query, MCP server with security, Aspire hosting, and Roslyn analyzers.

**Key capabilities:**
- Core domain types: `SymbolId`, `SymbolKind`, `DocComment`, `SourceSpan`, `SymbolNode`, `SymbolEdge`, `SymbolGraphSnapshot`
- Core interfaces: `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `ISearchIndex`, `IKnowledgeQueryService`
- Ingestion pipeline: local filesystem source, XML doc parser, Roslyn symbol collector
- BM25 keyword search index
- Snapshot store with MessagePack serialization and determinism guarantee
- MCP server with `PathAllowlist`, `AuditLogger`, `PromptInjectionScanner`
- Aspire app host (`DocAgent.AppHost`) with configuration and telemetry wiring
- Roslyn analyzers: `DocCoverageAnalyzer`, `DocParityAnalyzer`, `SuspiciousEditAnalyzer`
- Ingestion trigger via MCP

**MCP tools delivered:**
| Tool | Description |
|------|-------------|
| `search_symbols` | Search by keyword |
| `get_symbol` | Full symbol detail by SymbolId |
| `get_references` | Symbols referencing a given symbol |
| `diff_snapshots` | Diff two snapshot versions |
| `explain_project` | Project overview in one call |
| `ingest_project` | Runtime ingestion trigger for a project |

---

### v1.1 — Semantic Diff and Change Intelligence (shipped 2026-03-01)

Semantic diffing engine, incremental file-level ingestion, and change review tools.

**Key capabilities:**
- `SymbolGraphDiffer`: symbol-level semantic diffs (signature, nullability, constraints, accessibility, documentation)
- `ChangeReviewer`: groups changes by severity, detects unusual patterns
- `IncrementalIngestionEngine`: SHA-256 file manifest, skips unchanged files
- Security: `PathAllowlist` applied to ChangeTools

**MCP tools delivered:**
| Tool | Description |
|------|-------------|
| `review_changes` | Review all changes grouped by severity with pattern detection |
| `find_breaking_changes` | Public API breaking changes (CI-optimized) |
| `explain_change` | Human-readable explanation of changes to a specific symbol |

---

### v1.2 — Multi-Project and Solution-Level Graphs (shipped 2026-03-02)

Solution-level ingestion, cross-project edges, stub nodes, and project-aware search.

**Key capabilities:**
- `SolutionSnapshot`, `ProjectEntry` domain types for solution-level graphs
- Cross-project reference edges and stub nodes for external symbols
- Project-aware search with FQN disambiguation
- `SolutionIngestionService`: ingests entire `.sln` with language filtering and TFM dedup
- Solution-level diff and architecture overview

**MCP tools delivered:**
| Tool | Description |
|------|-------------|
| `explain_solution` | Solution-level architecture overview |
| `diff_solution_snapshots` | Solution-level diff across all projects |
| `ingest_solution` | Ingest an entire .sln solution |

---

### v1.3 — Housekeeping (shipped 2026-03-03)

Incremental solution re-ingestion, performance benchmarks, code cleanup, and documentation refresh.

**Key capabilities:**
- `IncrementalSolutionIngestionService`: decorator over `SolutionIngestionService` with pointer-file state
- Dependency cascade detection for cross-project edge staleness
- BenchmarkDotNet performance benchmarks with regression guard
- OpenTelemetry instrumentation (meters, spans) via `EmitTelemetry` helper
- Stale comment and dead-code cleanup across all projects
- Documentation refresh (this document)

---

## Future Milestones

### v1.5 — Package Mapping

- Parse `*.csproj`, `Directory.Packages.props`, `packages.lock.json`, `*.nuspec`
- Produce `PackageRefGraph` (project → package → version)
- Reference resolver for canonical IDs and metadata
- Requirements: PKG-01, PKG-02

### Future Considerations

| Area | Description | Status |
|------|-------------|--------|
| Polyglot support | Tree-sitter and LSP-based ingestion for non-.NET languages | Speculative |
| Embeddings / vector index | `IVectorIndex` interface exists; no implementation | Deferred (provider TBD) |
| Query DSL | Structured query language over the symbol graph | Speculative |
| Remote git sources | Clone/fetch to read-only cache, branch/tag pinning | Deferred |
| NuGet metadata source | Minimal docs ingestion from NuGet packages | V3 aspiration |
| Source generators | Emit symbol graph hints, generate reference stubs | V3 aspiration |
| Cross-repo workspace | Merge multiple snapshots into a unified knowledge space | V3 aspiration |

---

## Design Principles

- Pipeline as compiler: discover → parse → normalize → index → serve
- Snapshots are immutable and deterministic (same input → identical output)
- Interfaces are stable and versioned; implementations are pluggable
- No implicit network calls in tests
- Strong typing throughout; explicit versioning on all contracts
