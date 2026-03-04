# Milestones

## v1.0 MVP (Shipped: 2026-02-28)

**Phases completed:** 8 phases, 24 plans, 8 tasks

**Timeline:** 3 days (2026-02-25 → 2026-02-28) | 121 commits | 4,391 LOC src + 4,747 LOC tests

**Key accomplishments:**
- Compiler-grade symbol graph with deterministic MessagePack serialization and XxHash content hashing
- Roslyn-backed ingestion pipeline with XML doc parsing, inheritdoc expansion, and edge-case handling
- BM25 search index with CamelCase-aware tokenization and FSDirectory persistence
- Query facade with search, get-symbol, diff (including rename detection via PreviousIds), and get-references
- Five MCP tools (search_symbols, get_symbol, get_references, diff_snapshots, explain_project) with security boundaries
- Path allowlists, audit logging, and prompt injection defense
- Roslyn analyzers for doc parity, suspicious edits, and coverage enforcement
- Aspire app host with OpenTelemetry tracing and health probes
- Runtime ingestion trigger via ingest_project MCP tool
- 177 passing tests (unit + integration + E2E)

---


## v1.1 Semantic Diff & Change Intelligence (Shipped: 2026-03-01)

**Phases completed:** 4 phases, 9 plans

**Timeline:** 2 days (2026-02-28 → 2026-03-01) | 44 commits | 42 C# files changed, +4,507 LOC

**Key accomplishments:**
- Symbol-level semantic diff engine detecting signature, nullability, constraint, accessibility, dependency, and doc changes across snapshots
- Incremental ingestion with SHA-256 file change detection — only re-parses changed files, proven identical to full re-ingestion
- ChangeReviewer with four unusual-pattern detectors (mass signature change, nullability regression, accessibility escalation, public API removal) and three-tier severity escalation
- Three new MCP tools: `review_changes`, `find_breaking_changes`, `explain_change` with json/markdown/tron output formats
- PathAllowlist security enforcement on all ChangeTools methods, closing audit gap to match DocTools/IngestionTools pattern
- 24 diff-specific tests + 4 incremental correctness tests + 10 ChangeTools tests + 3 security gate tests

---


## v1.2 Multi-Project & Solution-Level Graphs (Shipped: 2026-03-02)

**Phases completed:** 6 phases, 11 plans, 0 tasks

**Timeline:** 2 days (2026-03-01 → 2026-03-02) | 67 commits | 14 C# files changed, +1,494 LOC

**Key accomplishments:**
- Extended core domain types with solution-level identity (SolutionSnapshot, ProjectEntry, ProjectEdge, NodeKind, EdgeScope) with full MessagePack backward compatibility
- Built SolutionIngestionService: ingest entire .sln files with language filtering, TFM dedup, MSBuild failure handling, and PathAllowlist security
- Enriched solution graphs with cross-project edge classification, project dependency DAG, and stub node synthesis for NuGet types
- Made BM25 and InMemory indexes project-aware with stub node filtering to prevent search pollution
- Extended all MCP tools (search_symbols, get_symbol, get_references) with project filtering, FQN disambiguation, and cross-project query support
- Added explain_solution and diff_solution_snapshots MCP tools for solution-level architecture overview and diffing

### Known Gaps
- INGEST-05: Per-project incremental re-ingestion (Phase 17 — deferred to v1.3)

---


## v1.3 Housekeeping (Shipped: 2026-03-04)

**Phases completed:** 4 phases, 8 plans

**Timeline:** 3 days (2026-03-02 → 2026-03-04) | 28 commits

**Key accomplishments:**
- Incremental solution re-ingestion with per-project SHA-256 manifest comparison, dependency cascade (topological sort + dirty propagation), and byte-identity determinism guarantee (INGEST-05)
- BenchmarkDotNet performance suite measuring MSBuild workspace open, full solution ingestion, and incremental no-change latency with regression guard tests
- Stale code cleanup: removed dead TODO comments, resolved v1.2 audit artifact frontmatter issues, wired benchmark to IncrementalSolutionIngestionService decorator, registered OTel meter
- Documentation refresh: Architecture.md (6 projects, 12 MCP tools, Mermaid diagrams), Plan.md (v1.0–v1.2 shipped milestone history), Testing.md (330/309/21 test counts, category breakdown, known limitations)

---

