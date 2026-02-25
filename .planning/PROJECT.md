# DocAgentFramework

## What This Is

A .NET 10 / C# framework that turns code documentation (XML docs) and code structure (Roslyn symbols) into agent-consumable memory, exposed via a securable MCP server. Agents reason over compiler truth — not approximations — through a versioned, diffable symbol graph and narrow tool surface.

## Core Value

Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Ingest XML documentation files (`GenerateDocumentationFile` output) and bind to stable symbol identifiers
- [ ] Walk Roslyn symbols (namespaces, types, members) with file spans and source anchors
- [ ] Normalize ingested data into a versioned `SymbolGraphSnapshot` with deterministic serialization
- [ ] Build a BM25/keyword search index over symbol graph snapshots
- [ ] Expose `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project` MCP tools via stdio transport
- [ ] Secure MCP server with path allowlists, audit logging, and input validation
- [ ] Host via Aspire app host with configuration and telemetry wiring
- [ ] Roslyn analyzers that detect public API changes not reflected in docs
- [ ] Roslyn analyzers that detect suspicious edits (semantic changes without doc/test updates)
- [ ] Doc coverage policy enforcement for public symbols
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

- Scaffold repo exists with architecture docs, domain contracts in code, and stub implementations
- Core types already defined: `SymbolId`, `SymbolNode`, `SymbolEdge`, `SymbolGraphSnapshot`, `DocComment`, `SourceSpan`
- Interfaces defined: `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, `ISearchIndex`, `IKnowledgeQueryService`
- Stub MCP server running with `ModelContextProtocol` SDK (stdio transport, two stub tools)
- `InMemorySearchIndex` has basic contains-match; needs BM25 replacement
- `XmlDocParser` stores raw XML content; needs proper parsing and symbol binding
- Test project exists with stub tests for parser and search index
- Extended plans documented in `EXTENDED_PLANS/` for future tiers (live interrogation, polyglot, enterprise)

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
| Non-generic `ISymbolGraphBuilder` for V1, refactor to generic when adding polyglot | Simpler V1 contract; generic needed only when multiple doc source types exist | — Pending |
| Stdio-only MCP transport for V1+V2 | Simplest security model; non-stdio deferred | — Pending |
| Package mapping deferred to V1.5 | Focus V1 on XML docs + Roslyn + MCP core pipeline | — Pending |
| BM25 keyword index first, embeddings behind interface | Cheap/fast/deterministic; embeddings provider TBD | — Pending |
| Snapshot artifacts written to `artifacts/` directory | Simple file-based storage for V1; SQLite optional | — Pending |

---
*Last updated: 2026-02-25 after initialization*
