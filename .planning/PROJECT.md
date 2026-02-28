# DocAgentFramework

## What This Is

A .NET 10 / C# framework that turns code documentation (XML docs) and code structure (Roslyn symbols) into agent-consumable memory, exposed via a securable MCP server. Agents reason over compiler truth — not approximations — through a versioned, diffable symbol graph and narrow tool surface.

## Core Value

Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

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

### Active

- [ ] Symbol-level semantic diff engine (signature, nullability, constraints, accessibility, dependency changes)
- [ ] Unusual Change Review skill: compare snapshots, flag suspicious diffs, propose worktree-based remediation
- [ ] `review_changes` MCP tool returning structured findings

### Out of Scope

- Package mapping (csproj, lock files, nuspec → PackageRefGraph) — deferred to V1.5
- Embeddings/vector index — deferred; keep `IVectorIndex` interface only
- Non-stdio MCP transports (HTTP, SSE) — deferred to later
- Polyglot support (Tree-sitter, LSP bridge) — future tier
- Source generators — V3
- Query DSL over symbol graph — speculative/long-term
- Structural code rewrite engine — speculative/long-term

## Context

Shipped v1.0 with 4,391 LOC source + 4,747 LOC tests (C#). 177 passing tests.

Tech stack: .NET 10, Roslyn 4.12.0, Lucene.Net 4.8 (BM25), MessagePack 3.1.4, ModelContextProtocol SDK, Aspire, OpenTelemetry.

Architecture: discover → parse → normalize → index → serve (6 projects: Core, Ingestion, Indexing, McpServer, AppHost, Tests).

Full pipeline operational: `ingest_project` MCP tool → Roslyn symbol walk → XML doc parsing → deterministic snapshot → BM25 indexing → query via 5 MCP tools.

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

---
*Last updated: 2026-02-28 after v1.0 milestone*
