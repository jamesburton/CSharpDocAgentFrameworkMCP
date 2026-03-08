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

## Milestone: v1.2 — Multi-Project & Solution-Level Graphs

**Shipped:** 2026-03-02
**Phases:** 6 | **Plans:** 11 | **Commits:** 67

### What Was Built
- Solution-level domain types (SolutionSnapshot, ProjectEntry, ProjectEdge) with backward-compatible MessagePack serialization
- SolutionIngestionService: ingest entire .sln files with language filtering, TFM dedup, MSBuild failure handling
- Cross-project edge classification and stub node synthesis for NuGet type references
- Stub node filtering at index time (BM25 + InMemory) to prevent search pollution
- Project-aware MCP tools: project filter, FQN disambiguation, cross-project references
- Two new MCP tools (explain_solution, diff_solution_snapshots) for solution-level architecture overview and diffing
- 303 passing tests (83 new across 6 phases)

### What Worked
- Gap closure phases (14.1 and 18) integrated cleanly — audit-driven pattern now well-established
- PipelineOverride seam pattern enabled comprehensive testing of SolutionIngestionService without MSBuild dependency
- Building on v1.1 patterns (opaque denial, tool class structure) kept SolutionTools consistent
- Re-audit after Phase 18 gave clean confirmation before milestone completion

### What Was Inefficient
- Phase 17 (incremental solution re-ingestion) was scoped into v1.2 but never started — should have been flagged as stretch goal earlier
- ROADMAP.md Phase Details had duplicate plan listings for Phases 16 and 17 (copy-paste from Phase 15)
- Traceability table in REQUIREMENTS.md showed some GRAPH requirements as "Partial" even after Phase 14.1 completed them

### Patterns Established
- ProjectWalkContext readonly record struct for shared state across solution project walks
- Primitive framework type filter (30 common types) to cap stub node count
- ExtractProjectSnapshot helper for decomposing flat snapshots back into per-project views
- FQN heuristic: input without pipe `|` treated as FQN candidate (SymbolIds always contain `|`)

### Key Lessons
1. Milestone audits continue to prove value — Phase 14.1 (gap closure) and Phase 18 (tool name collision) both created by audit findings
2. Scope management: defer stretch goals early rather than leaving them as unstarted phases at milestone completion
3. Wire name collisions between tool classes should be caught by automated registration tests, not manual audit
4. PipelineOverride/BuildOverride seam pattern is the gold standard for testing services that wrap expensive external tools (MSBuild, Roslyn)

### Cost Observations
- Model mix: primarily sonnet for execution, opus for planning/verification/audit
- Timeline: 2 days (2026-03-01 → 2026-03-02) for 6 phases, 11 plans
- Notable: Gap closure phases (14.1 and 18) were small and fast — audit-driven micro-phases work well

---

## Milestone: v1.3 — Housekeeping

**Shipped:** 2026-03-04
**Phases:** 4 | **Plans:** 8 | **Commits:** 28

### What Was Built
- Incremental solution re-ingestion: per-project SHA-256 manifest comparison, dependency cascade via topological sort, byte-identity determinism guarantee (INGEST-05)
- BenchmarkDotNet performance suite: MSBuild workspace open, full solution ingestion, incremental no-change latency with regression guard tests
- Stale code cleanup: dead TODO comments, v1.2 audit artifact frontmatter, benchmark wiring to decorator, OTel meter registration
- Documentation refresh: Architecture.md (6 projects, 12 MCP tools, Mermaid diagrams), Plan.md (milestone history), Testing.md (330/309/21 counts)

### What Worked
- Gap closure phases (21, 22) created directly from v1.3 milestone audit — audit-driven pattern now fully mature
- Research phase for Phase 22 produced ground truth data that prevented hallucinated doc content
- Separating benchmark project with relaxed warnings avoided version conflicts with BDN transitive deps
- Pointer file pattern for incremental state was simple and effective

### What Was Inefficient
- v1.3 audit ran before phases 21-22 existed, so audit showed gaps_found for work that was already planned — audit timing could be smarter
- REQUIREMENTS.md QUAL-01/02/03 checkboxes not auto-updated when Phase 21 completed — required manual fix during milestone completion

### Patterns Established
- Decorator pattern for incremental services (IncrementalSolutionIngestionService wraps SolutionIngestionService)
- Solution-relative path keys with __ separator for manifest filename collision avoidance
- Pointer file pattern (latest-{sln}.ptr) for referencing previous snapshots
- Research-first documentation: ground truth gathered before writing prevents phantom features

### Key Lessons
1. Research phases before documentation tasks prevent hallucination — 22-RESEARCH.md was essential for accurate docs
2. Decorator pattern cleanly separates incremental logic from core ingestion — easier to test each independently
3. Audit timing: run audit only after all planned phases complete, not mid-milestone
4. REQUIREMENTS.md should be auto-updated by verification — manual checkbox management is error-prone

### Cost Observations
- Model mix: primarily sonnet for execution, opus for planning/verification
- Timeline: 3 days (2026-03-02 → 2026-03-04) for 4 phases
- Notable: Housekeeping milestone was efficient — small focused phases with clear scope

---

## Milestone: v1.5 — Robustness

**Shipped:** 2026-03-08
**Phases:** 5 | **Plans:** 7 | **Commits:** 34

### What Was Built
- Roslyn 4.14.0 unified across all projects with centralized NuGetAudit and zero VersionOverride hacks
- O(1) symbol lookup, edge traversal, and metadata caching via private SnapshotLookup dictionaries
- Startup configuration validation with fail-fast IHostedLifecycleService before MCP transport accepts connections
- Token-bucket rate limiting with separate query/ingestion buckets and structured error responses
- Three new MCP tools: paginated get_references, find_implementations, get_doc_coverage
- CLAUDE.md updated to complete 14-tool MCP reference with parameter signatures verified against source
- 335+ tests, 20,400 LOC total

### What Worked
- Parallel worktree execution of Phase 24 (Indexing) and Phase 25 (McpServer) — different file sets, no conflicts
- Build order research (PKG → PERF/OPS parallel → API → docs) correctly reflected technical dependencies
- Private nested class pattern for SnapshotLookup avoided public API surface expansion while delivering O(1) performance
- Existing test suite (335 tests) served as implicit verification for Phase 24's internal optimization — no new tests needed
- Backward-compatible pagination (limit=0 returns all) avoided breaking existing callers

### What Was Inefficient
- No VERIFICATION.md files created during execution — all 5 phases required retroactive Nyquist validation
- Milestone audit initially classified as tech_debt due to missing VERIFICATION.md files, then upgraded to passed after validation
- s_docKinds/s_docAccessibilities constants duplicated between DocTools and SolutionTools — no shared base class to avoid duplication

### Patterns Established
- Private nested SnapshotLookup class with pre-built dictionaries for O(1) hot-path access
- Content-hash-based cache invalidation for snapshot-derived state
- IHostedLifecycleService.StartingAsync as earliest validation hook pattern
- MCP filter chain ordering: rate limit → audit (early rejection saves work)
- Separate rate limit buckets per tool category (query vs ingestion)
- Pagination envelope: total (returned) + totalCount (available) + offset + limit

### Key Lessons
1. Internal optimizations (private classes) don't need new tests when existing tests implicitly cover the behavior
2. VERIFICATION.md should be created during phase execution, not retroactively — process gap persisted across all 5 phases
3. Parallel worktree execution works well when file ownership is clearly separated (different projects)
4. Rate limiting filter ordering matters — placing it before audit filter saves computation on rejected requests
5. Backward-compatible pagination (limit=0 = return all) is the right default when adding pagination to existing APIs

### Cost Observations
- Model mix: primarily sonnet for execution, opus for planning/verification
- Timeline: 4 days (2026-03-04 → 2026-03-08) for 5 phases, 7 plans
- Notable: Smallest plan count (7) but highest impact — production-grade operational infrastructure

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Commits | Phases | Key Change |
|-----------|---------|--------|------------|
| v1.0 | 121 | 8 | Established full GSD workflow with audit-driven gap closure |
| v1.1 | 44 | 4 | Smaller scope, audit-driven gap closure pattern repeated |
| v1.2 | 67 | 6 | Gap closure phases (14.1, 18) as first-class workflow; deferred stretch goal |
| v1.3 | 28 | 4 | Research-first docs; audit-driven gap closure phases fully mature |
| v1.5 | 34 | 5 | Parallel worktree execution; production-grade infra (rate limiting, startup validation) |

### Cumulative Quality

| Milestone | Tests | LOC (src) | LOC (tests) |
|-----------|-------|-----------|-------------|
| v1.0 | 177 | 4,391 | 4,747 |
| v1.1 | ~220 | ~8,900 | ~9,250 |
| v1.2 | 303 | ~8,170 | — |
| v1.3 | 330 | ~8,500 | — |
| v1.5 | 335+ | ~20,400 | — |

### Top Lessons (Verified Across Milestones)

1. Milestone audits catch integration gaps that per-phase testing misses (v1.0: env var + forceReindex, v1.1: PathAllowlist gap, v1.2: tool name collision + graph enrichment gaps)
2. E2E tests through real DI are the strongest proof of pipeline correctness
3. Pure static services are the right default for algorithmic code — verified across SymbolGraphDiffer, ChangeReviewer, FileHasher
4. Seam-based test isolation (PipelineOverride, BuildOverride) enables comprehensive testing of expensive-dependency services — verified across v1.1, v1.2, and v1.3
5. Research phases before documentation prevent hallucination — verified in v1.3 (ground truth data produced accurate docs)
6. VERIFICATION.md should be created during phase execution, not retroactively — v1.5 had to validate all 5 phases post-hoc
7. Parallel worktree execution is effective when file ownership is clearly separated — verified in v1.5 (Phase 24 Indexing ∥ Phase 25 McpServer)
