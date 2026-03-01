# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — MVP

**Shipped:** 2026-02-28
**Phases:** 8 | **Plans:** 24 | **Commits:** 121

### What Was Built
- Full discover → parse → normalize → index → serve pipeline for .NET codebases
- 5 MCP tools (search_symbols, get_symbol, get_references, diff_snapshots, explain_project) with security boundaries
- BM25 search index with CamelCase-aware tokenization and disk persistence
- Roslyn analyzers for doc parity, suspicious edits, and coverage enforcement
- Aspire app host with OpenTelemetry tracing
- Runtime ingestion trigger via ingest_project MCP tool
- 177 passing tests (unit + integration + E2E)

### What Worked
- Phase ordering (core → ingestion → indexing → query → serving) prevented cascading rework
- Deterministic snapshot serialization (MessagePack + XxHash) caught integration issues early
- Content-addressed SnapshotStore simplified versioning — no explicit version management needed
- Milestone audit after initial 6 phases caught real integration gaps (env var mismatch, forceReindex no-op) before shipping
- E2E integration tests in Phase 7 proved the full pipeline works through real DI container

### What Was Inefficient
- Phase execution order (5 → 7 → 6 → 8) diverged from original plan (5 → 6 → 7 → 8) due to integration gaps discovered during audit
- `init` vs `set` on DocAgentServerOptions only caught during E2E testing — could have been caught earlier with DI smoke tests
- InMemorySearchIndex persisted longer than needed — should have been replaced in Phase 3

### Patterns Established
- Closure-based singleton path resolution for shared directory dependencies
- Async iterator exception pattern (throw before yield in IAsyncEnumerable)
- Content-addressed artifact storage with manifest.json index
- Per-test DI container construction for integration test isolation
- AuditFilter middleware pattern for cross-cutting MCP tool concerns

### Key Lessons
1. Milestone audit is essential — it caught 2 real integration gaps that would have broken production use
2. E2E tests through real DI are worth the setup cost — they catch config/wiring issues unit tests miss
3. Options classes need `set` (not `init`) for IOptions Configure pattern — universal .NET convention
4. Phase ordering matters more than parallelism — correct dependency order prevents rework

### Cost Observations
- Model mix: primarily sonnet for execution, opus for planning/verification
- Timeline: 3 days from scaffold to shipped v1.0
- Notable: Wave-based parallel execution worked well for independent plans within phases

---

## Milestone: v1.1 — Semantic Diff & Change Intelligence

**Shipped:** 2026-03-01
**Phases:** 4 | **Plans:** 9 | **Commits:** 44

### What Was Built
- Symbol-level semantic diff engine detecting 6 change categories across SymbolGraphSnapshots
- Incremental ingestion with SHA-256 file change detection — only changed files re-parsed
- ChangeReviewer with 4 unusual-pattern detectors and 3-tier severity escalation
- 3 new MCP tools (review_changes, find_breaking_changes, explain_change) with json/markdown/tron output
- PathAllowlist security enforcement on all ChangeTools, closing audit gap
- ~41 new tests across diff, incremental, change intelligence, and security

### What Worked
- Milestone audit after Phase 11 caught the PathAllowlist security gap — fixed cleanly as Phase 12
- Pure static classes (SymbolGraphDiffer, ChangeReviewer, FileHasher) simplified testing and composition
- Incremental correctness tests (proving incremental == full) gave high confidence in the change detection pipeline
- Building on v1.0 patterns (opaque denial, ContractlessStandardResolver conventions) made new code consistent

### What Was Inefficient
- Phase 12 (security gap closure) could have been avoided if Phase 11 had a checklist to verify security patterns from peer tool classes
- ROADMAP.md progress table had formatting drift (missing milestone column in rows 10-12) — caught during archival

### Patterns Established
- Per-category nullable detail fields for MessagePack-safe polymorphic diff types
- BuildOverride hook pattern for test isolation of ingestion engine
- Opaque not_found denial as unified security pattern across all MCP tool classes
- ContentHashedBuilder for integration tests that need deterministic symbol graphs without real Roslyn

### Key Lessons
1. Milestone audits continue to prove their value — Phase 12 only existed because the audit caught a real security gap
2. Pure static services with no DI are the right default for algorithmic code — simpler to test, compose, and reason about
3. Security enforcement should be a checklist item on every MCP tool class, not discovered via audit

### Cost Observations
- Model mix: primarily sonnet for execution, opus for planning/verification
- Timeline: 2 days (2026-02-28 → 2026-03-01) for 4 phases
- Notable: Smaller milestone (4 phases vs 8) shipped faster with less rework

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Commits | Phases | Key Change |
|-----------|---------|--------|------------|
| v1.0 | 121 | 8 | Established full GSD workflow with audit-driven gap closure |
| v1.1 | 44 | 4 | Smaller scope, audit-driven gap closure pattern repeated |

### Cumulative Quality

| Milestone | Tests | LOC (src) | LOC (tests) |
|-----------|-------|-----------|-------------|
| v1.0 | 177 | 4,391 | 4,747 |
| v1.1 | ~220 | ~8,900 | ~9,250 |

### Top Lessons (Verified Across Milestones)

1. Milestone audits catch integration gaps that per-phase testing misses (v1.0: env var + forceReindex, v1.1: PathAllowlist gap)
2. E2E tests through real DI are the strongest proof of pipeline correctness
3. Pure static services are the right default for algorithmic code — verified across SymbolGraphDiffer, ChangeReviewer, FileHasher
