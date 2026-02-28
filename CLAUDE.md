# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DocAgentFramework is a .NET 10 / C# scaffold that ingests code documentation (XML docs + Roslyn symbol info), normalizes it into a queryable symbol graph, and serves it via a securable MCP server. This is a **scaffold repo** — architecture, contracts, and test stubs are in place; implementations are being built out in parallel worktrees.

## Build & Test Commands

```bash
# Build and run all tests
dotnet test

# Build only
dotnet build src/DocAgentFramework.sln

# Run a single test class
dotnet test --filter "FullyQualifiedName~XmlDocParserTests"

# Run MCP server (stdio transport)
dotnet run --project src/DocAgent.McpServer

# Run under Aspire app host
dotnet run --project src/DocAgent.AppHost
```

## Build Configuration

- **Target:** .NET 10 (`net10.0`) with `LangVersion=preview`
- **Nullable:** enabled, **TreatWarningsAsErrors:** true
- **Central package management** via `src/Directory.Packages.props`
- **Test framework:** xUnit + FluentAssertions

## Architecture

Pipeline: **discover → parse → normalize → index → serve**

Six layers with strict boundaries:

| Layer | Project | Responsibility |
|-------|---------|----------------|
| Core | `DocAgent.Core` | Pure domain types + interfaces (no IO) |
| Ingestion | `DocAgent.Ingestion` | Source discovery, XML parsing, normalization |
| Indexing | `DocAgent.Indexing` | Build/query search indexes from snapshots |
| Serving | `DocAgent.McpServer` | MCP tools + security boundaries |
| Host | `DocAgent.AppHost` | Aspire app host, config, telemetry wiring |
| Tests | `DocAgent.Tests` | Unit + component tests |

### Key Domain Types (DocAgent.Core)

- **`SymbolGraphSnapshot`** — versioned immutable graph of `SymbolNode`s and `SymbolEdge`s
- **`SymbolNode`** — a code symbol (type, method, property, etc.) with `DocComment` and `SourceSpan`
- **Core interfaces:** `IProjectSource` → `IDocSource` → `ISymbolGraphBuilder` → `ISearchIndex` → `IKnowledgeQueryService`

### MCP Tools (planned surface)

`search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`

## Development Conventions

- Use a **dedicated worktree per task** (see `docs/Worktrees.md` for commands)
- Keep edits minimal and incremental; tests must pass continuously
- Snapshot outputs must be **deterministic** (same input → identical output)
- No implicit network calls in tests
- Strong typing and explicit versioning on all contracts
- Done = unit tests added + `dotnet test` passes + docs updated where relevant

## Key Documentation

- `docs/Architecture.md` — layer contracts and interface definitions
- `docs/Plan.md` — V1–V3 implementation plan with worktree parallelization
- `docs/Security.md` — MCP security model (default-deny, audit logging, sandboxed paths)
- `docs/Testing.md` — test strategy and acceptance criteria
- `docs/Worktrees.md` — worktree conventions and commands
- `EXTENDED_PLANS/` — extended research, polyglot strategy, tooling matrix
