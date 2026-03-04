# DocAgentFramework

## What This Is

A .NET 10 / C# framework that turns code documentation (XML docs) and code structure (Roslyn symbols) into agent-consumable memory, exposed via a securable MCP server. Agents reason over compiler truth — not approximations — through a versioned, diffable symbol graph with solution-wide scope and narrow tool surface.

## Core Value

Agents can query a stable, compiler-grade symbol graph of any .NET codebase — from single projects to entire solutions — via MCP tools, getting precise answers about types, members, relationships, and documentation.

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

### Active

## Current Milestone: v1.5 Robustness

**Goal:** Harden the query pipeline, extend the tool surface, upgrade dependencies, and polish operational readiness — making DocAgentFramework production-grade.

**Target features:**
- CLAUDE.md refresh with all 12 tools, startup config validation, rate limiting
- Roslyn 4.14 upgrade, full package audit
- Pagination for large result sets, find_implementations tool, doc coverage metrics tool
- O(1) symbol lookup, batched project resolution, search metadata caching

### Out of Scope
- Package mapping (csproj, lock files, nuspec → PackageRefGraph) — deferred to V1.5
- Embeddings/vector index — deferred; keep `IVectorIndex` interface only
- Non-stdio MCP transports (HTTP, SSE) — deferred to later
- Polyglot support (Tree-sitter, LSP bridge) — future tier
- Source generators — V3
- Query DSL over symbol graph — speculative/long-term
- Structural code rewrite engine — speculative/long-term

## Context

Shipped v1.3 with ~8,500 LOC C#. 330 tests (309 passing, 21 environment-dependent).

Tech stack: .NET 10, Roslyn 4.12.0, Lucene.Net 4.8 (BM25), MessagePack 3.1.4, MSBuildWorkspace, ModelContextProtocol SDK, Aspire, OpenTelemetry, BenchmarkDotNet, SHA-256 file hashing.

Architecture: discover → parse → normalize → index → serve → diff → review (6 projects: Core, Ingestion, Indexing, McpServer, AppHost, Analyzers + Benchmarks project).

Full pipeline operational: 12 MCP tools (`search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`, `review_changes`, `find_breaking_changes`, `explain_change`, `ingest_project`, `ingest_solution`, `explain_solution`, `diff_solution_snapshots`). Incremental ingestion at both file and solution levels — unchanged projects skipped via SHA-256 manifest comparison with dependency cascade. All tools secured with PathAllowlist enforcement. Performance baselined with BenchmarkDotNet regression guards.

## Constraints

- **Target framework**: .NET 10 (`net10.0`), `LangVersion=preview`, nullable enabled, warnings-as-errors
- **Central packages**: managed via `src/Directory.Packages.props`
- **MCP SDK**: `ModelContextProtocol` preview package
- **Roslyn**: `Microsoft.CodeAnalysis.CSharp` 4.12.0
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
| diff_solution_snapshots wire name (not diff_snapshots) | Avoids tool name collision with DocTools | ✓ Good — 12 unique MCP tool names |
| DependencyCascade extracted from SolutionIngestionService | Reusable topological sort + dirty set computation | ✓ Good — clean separation |
| Solution-relative path keys with __ separator for manifests | Prevents filename collision for same-name projects in different directories | ✓ Good — unique keys guaranteed |
| IncrementalSolutionIngestionService as decorator | Wraps SolutionIngestionService; skip path returns cached snapshot | ✓ Good — single-responsibility |
| Pointer file pattern (latest-{sln}.ptr) | Reference previous snapshot for incremental comparison | ✓ Good — simple file-based state |
| BenchmarkDotNet in separate project with relaxed warnings | BDN transitive Roslyn deps conflict with pinned 4.12.0; measurement ≠ production | ✓ Good — isolation prevents version conflicts |
| Dict-keyed BaselineModels for baselines.json | Matches actual BDN output schema | ✓ Good — no schema mismatch |

---
*Last updated: 2026-03-04 after v1.5 Robustness milestone start*
