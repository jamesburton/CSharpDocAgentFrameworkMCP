# DocAgentFramework

## What This Is

A .NET 10 / C# framework that turns code documentation and code structure into agent-consumable memory, exposed via a securable MCP server. Supports both .NET (Roslyn) and TypeScript (Compiler API via Node.js sidecar) codebases. Agents reason over compiler truth — not approximations — through a versioned, diffable symbol graph with solution-wide scope, 15-tool MCP surface, and production-grade operational infrastructure.

## Core Value

Agents can query a stable, compiler-grade symbol graph of any .NET or TypeScript codebase — from single projects to entire solutions — via MCP tools, getting precise answers about types, members, relationships, and documentation.

## Requirements

### Validated

- ✓ Ingest XML documentation files and bind to stable symbol identifiers — v1.0
- ✓ Walk Roslyn symbols (namespaces, types, members) with file spans and source anchors — v1.0
- ✓ Normalize ingested data into a versioned `SymbolGraphSnapshot` with deterministic serialization — v1.0
- ✓ Build a BM25/keyword search index over symbol graph snapshots — v1.0
- ✓ Expose `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project` MCP tools via stdio transport — v1.0
- ✓ Secure MCP server with path allowlists, audit logging, and input validation — v1.0
- ✓ Host via Aspire app host with configuration and telemetry wiring — v1.0
- ✓ Roslyn analyzers that detect public API changes not reflected in docs — v1.0
- ✓ Roslyn analyzers that detect suspicious edits (semantic changes without doc/test updates) — v1.0
- ✓ Doc coverage policy enforcement for public symbols — v1.0
- ✓ Runtime ingestion trigger via MCP tool — v1.0
- ✓ Symbol-level semantic diff engine (signature, nullability, constraints, accessibility, dependency changes) — v1.1
- ✓ Incremental ingestion with SHA-256 file change detection, only changed files re-parsed — v1.1
- ✓ Unusual Change Review skill: ChangeReviewer with four pattern detectors and severity escalation — v1.1
- ✓ `review_changes`, `find_breaking_changes`, `explain_change` MCP tools with json/markdown/tron output — v1.1
- ✓ PathAllowlist security enforcement on all ChangeTools methods — v1.1
- ✓ Solution-level domain types (SolutionSnapshot, ProjectEntry, ProjectEdge, NodeKind, EdgeScope) with backward compat — v1.2
- ✓ Ingest entire .sln solutions via `ingest_solution` MCP tool with language filtering, TFM dedup, MSBuild failure handling — v1.2
- ✓ Cross-project edge classification (EdgeScope.CrossProject) and stub node synthesis for NuGet types — v1.2
- ✓ Stub node filtering at index time to prevent BM25 search pollution — v1.2
- ✓ Project-aware search, get_symbol FQN disambiguation, cross-project get_references — v1.2
- ✓ `explain_solution` MCP tool for solution-level architecture overview — v1.2
- ✓ `diff_solution_snapshots` MCP tool for solution-level diff — v1.2
- ✓ PathAllowlist security on all SolutionTools methods — v1.2
- ✓ Per-project incremental solution re-ingestion with dependency cascade and byte-identity guarantee — v1.3
- ✓ BenchmarkDotNet performance baselines and regression guard for MSBuild workspace and solution ingestion — v1.3
- ✓ Stale code cleanup (dead TODOs, audit artifact frontmatter, benchmark wiring, OTel meter registration) — v1.3
- ✓ Documentation refresh (Architecture.md, Plan.md, Testing.md aligned to shipped reality) — v1.3
- ✓ Roslyn 4.14.0 unified across all projects with centralized NuGetAudit and zero VersionOverride — v1.5
- ✓ O(1) symbol lookup, edge traversal, and metadata caching via SnapshotLookup dictionaries — v1.5
- ✓ Startup configuration validation with fail-fast before MCP transport accepts connections — v1.5
- ✓ Token-bucket rate limiting with separate query/ingestion buckets — v1.5
- ✓ Paginated `get_references` with backward-compatible offset/limit defaults — v1.5
- ✓ `find_implementations` tool for interface/base-class hierarchy navigation — v1.5
- ✓ `get_doc_coverage` tool for documentation coverage metrics by project/namespace/kind — v1.5
- ✓ CLAUDE.md complete 14-tool MCP reference with parameter signatures verified against source — v1.5
- ✓ Node.js sidecar (`ts-symbol-extractor`) with NDJSON IPC, esbuild bundling, vitest tests — v2.0
- ✓ TypeScript Compiler API walker extracting all declaration types, relationships, docs, source spans — v2.0
- ✓ `ingest_typescript` MCP tool with PathAllowlist security and incremental SHA-256 hashing — v2.0
- ✓ All 14 existing MCP tools produce correct results against TypeScript snapshots — v2.0
- ✓ BM25 camelCase tokenizer for TypeScript symbol search alongside PascalCase C# — v2.0
- ✓ JSON contract alignment: string enums, JsonPropertyName, DocCommentConverter for TS↔C# fidelity — v2.0
- ✓ Aspire orchestration: Node.js sidecar as managed resource with health checks and dependency wiring — v2.0
- ✓ 657 tests including determinism, stress, robustness, cross-tool verification, golden-file deserialization — v2.0

### Active

(No active milestone — plan next with `/gsd:new-milestone`)

### Out of Scope
- Package mapping (csproj, lock files, nuspec → PackageRefGraph) — deferred
- Embeddings/vector index — deferred; keep `IVectorIndex` interface only
- Non-stdio MCP transports (HTTP, SSE) — no demand yet; security model not designed
- Polyglot support (Tree-sitter, LSP bridge) — future tier; .NET + TypeScript only for now
- Source generators — V3
- Query DSL over symbol graph — speculative/long-term
- Streaming MCP responses — MCP spec doesn't support streaming tool responses
- Per-client identity/auth — Stdio is single-client; auth not meaningful
- Live Roslyn SymbolFinder queries — v1.5 uses snapshot edges; live queries deferred
- Cross-language symbol graphs (C# ↔ TypeScript edges) — separate snapshots; no integration needed
- TypeScript declaration merging, re-export tracking, union/conditional type decomposition — tracked as v2.1 FUTR-01 through FUTR-08
- Warm sidecar process pooling — cold-start per ingestion is simpler; avoids memory leak risk
- Monorepo multi-tsconfig discovery — single tsconfig entry point for now

## Context

Shipped v2.0 with ~21,000 LOC C# + 710 LOC TypeScript. 657 tests (+ 2 gated sidecar E2E). 6 milestones shipped over 30 days (2026-02-25 → 2026-03-26).

Tech stack: .NET 10, Roslyn 4.14.0, Lucene.Net 4.8 (BM25), MessagePack 3.1.4, MSBuildWorkspace, ModelContextProtocol SDK, Aspire (+ Aspire.Hosting.JavaScript 13.1.2), OpenTelemetry, BenchmarkDotNet, SHA-256 file hashing, System.Threading.RateLimiting (TokenBucket), Node.js sidecar (`ts-symbol-extractor`), TypeScript Compiler API, esbuild, vitest.

Architecture: discover → parse → normalize → index → serve → diff → review (6 projects: Core, Ingestion, Indexing, McpServer, AppHost, Analyzers + Benchmarks project + `ts-symbol-extractor` Node.js sidecar). TypeScript extraction via Node.js sidecar with NDJSON IPC, orchestrated by Aspire.

Full pipeline operational: 15 MCP tools across 4 tool classes (DocTools: 7, ChangeTools: 3, IngestionTools: 3, SolutionTools: 2). Incremental ingestion at file, solution, and TypeScript project levels. All tools secured with PathAllowlist enforcement. Performance baselined with BenchmarkDotNet regression guards. O(1) query path via SnapshotLookup dictionaries. Startup validation and rate limiting for operational safety. TypeScript symbols queryable through same 14 query/change/solution tools as C#.

## Constraints

- **Target framework**: .NET 10 (`net10.0`), `LangVersion=preview`, nullable enabled, warnings-as-errors
- **Central packages**: managed via `src/Directory.Packages.props`
- **MCP SDK**: `ModelContextProtocol` preview package
- **Roslyn**: `Microsoft.CodeAnalysis.CSharp` 4.14.0
- **Testing**: xUnit + FluentAssertions
- **Determinism**: Same input must produce identical `SymbolGraphSnapshot` output
- **Security**: Default-deny toolset, path allowlists, audit logging, defense against prompt injection
- **No network in tests**: All tests use deterministic fixtures

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Non-generic `ISymbolGraphBuilder` for V1 | Simpler V1 contract; generic needed only when multiple doc source types exist | ✓ Good — clean single-type pipeline |
| Stdio-only MCP transport for V1 | Simplest security model; non-stdio deferred | ✓ Good — stdio works well for local agent use |
| BM25 keyword index first, embeddings behind interface | Cheap/fast/deterministic; embeddings provider TBD | ✓ Good — CamelCase tokenizer handles symbol search well |
| Snapshot artifacts to `artifacts/` directory | Simple file-based storage for V1 | ✓ Good — content-addressed store with manifest.json |
| MessagePack over JSON for snapshot serialization | Performance + determinism; contractless resolver | ✓ Good — byte-identical roundtrips verified |
| Closure-based singleton path resolution | Prevent SnapshotStore/BM25SearchIndex path divergence | ✓ Good — single GetDir() shared by both |
| DocAgentServerOptions `set` over `init` | Required for IOptions Configure lambda pattern | ✓ Good — discovered during E2E testing |
| Pure static SymbolGraphDiffer (no DI) | Stateless algorithm, deterministic output, no dependencies | ✓ Good — simple to test and compose |
| Per-category nullable detail fields in DiffTypes | MessagePack ContractlessStandardResolver safe; avoids polymorphic serialization | ✓ Good — clean pattern across 7 detail types |
| SHA-256 file hashing for incremental ingestion | Streaming async, lowercase hex, content-addressed change detection | ✓ Good — precise change detection, proven identical to full re-ingestion |
| ChangeReviewer as static pure-logic service | No DI needed; four detectors compose cleanly | ✓ Good — 9 tests, easy to extend |
| Opaque not_found denial for PathAllowlist | Consistent with DocTools/IngestionTools; no information leakage | ✓ Good — unified security pattern across all tool classes |
| Single flat snapshot for solution ingestion | Preserves existing contracts; per-project sub-snapshots would break consumers | ✓ Good — backward compat maintained |
| NodeKind.Real=0, EdgeScope.IntraProject=0 as enum defaults | MessagePack backward compat with v1.0/v1.1 artifacts | ✓ Good — old artifacts deserialize correctly |
| PipelineOverride seam for MSBuild-free tests | Full MSBuild bypass in unit tests; mirrors IngestionService pattern | ✓ Good — 7+ tests with zero MSBuild dependency |
| Stub nodes capped to direct PackageReference assemblies | Prevents index bloat from transitive closure | ✓ Good — manageable stub count |
| EdgeScope classified at construction time | No post-classification pass; locked design constraint | ✓ Good — clean single-pass pipeline |
| explain_solution derives DAG from CrossProject edges at query time | No pre-computed adjacency stored | ✓ Good — always fresh, no stale cache |
| diff_solution_snapshots wire name (not diff_snapshots) | Avoids tool name collision with DocTools | ✓ Good — 12→14 unique MCP tool names |
| DependencyCascade extracted from SolutionIngestionService | Reusable topological sort + dirty set computation | ✓ Good — clean separation |
| Solution-relative path keys with __ separator for manifests | Prevents filename collision for same-name projects in different directories | ✓ Good — unique keys guaranteed |
| IncrementalSolutionIngestionService as decorator | Wraps SolutionIngestionService; skip path returns cached snapshot | ✓ Good — single-responsibility |
| Pointer file pattern (latest-{sln}.ptr) | Reference previous snapshot for incremental comparison | ✓ Good — simple file-based state |
| BenchmarkDotNet in separate project with relaxed warnings | BDN transitive Roslyn deps conflict with pinned version; measurement ≠ production | ✓ Good — isolation prevents version conflicts |
| Dict-keyed BaselineModels for baselines.json | Matches actual BDN output schema | ✓ Good — no schema mismatch |
| SnapshotLookup as private nested class | No new public API surface for O(1) cache | ✓ Good — internal optimization, invisible to consumers |
| Cache keyed on ContentHash string equality | Simple and correct for immutable snapshot invalidation | ✓ Good — rebuild only when snapshot changes |
| AllowedPaths empty is warning not error | PathAllowlist defaults to cwd safely | ✓ Good — pragmatic default for development |
| IHostedLifecycleService.StartingAsync for startup validation | Earliest possible hook before MCP transport | ✓ Good — fail-fast before accepting connections |
| Separate query/ingestion rate limit buckets | Heavy querying cannot block ingestion and vice versa | ✓ Good — independent throughput control |
| RateLimitFilter before AuditFilter in pipeline | Early rejection saves computation | ✓ Good — less work wasted on rate-limited calls |
| limit=0 means return all in get_references | Backward compatibility with existing callers | ✓ Good — no silent truncation |
| find_implementations via existing GetReferencesAsync | Reuses edge traversal; no new query method needed | ✓ Good — minimal code surface |
| Tool docs organized by source file category in CLAUDE.md | Easy cross-reference between docs and source | ✓ Good — 4 categories match 4 tool classes |

---
*Last updated: 2026-03-26 after v2.0 TypeScript Language Support milestone shipped*
