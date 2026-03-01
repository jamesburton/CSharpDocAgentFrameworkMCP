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

