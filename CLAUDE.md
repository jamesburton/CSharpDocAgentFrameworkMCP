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

### MCP Tools (14 tools)

#### Query Tools (DocTools.cs)

**`search_symbols`** — Search symbols and documentation by keyword.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| query | string | (required) | Search query |
| kindFilter | string? | null | Symbol kind filter: Namespace, Type, Method, Property, Field, Event, Parameter, Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter |
| project | string? | null | Project name filter (exact match, case-sensitive). Omit for all projects. |
| offset | int | 0 | Result offset for pagination |
| limit | int | 20 | Result limit (max 100) |
| fullDocs | bool | false | Include full doc comments in results |
| format | string | "json" | Output format: json, markdown, tron |

**`get_symbol`** — Get full symbol detail by stable SymbolId.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| symbolId | string | (required) | Stable SymbolId (assembly-qualified) |
| includeSourceSpans | bool | false | Include source file path and line range |
| format | string | "json" | Output format: json, markdown, tron |

**`get_references`** — Get symbols that reference the given symbol.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| symbolId | string | (required) | Stable SymbolId |
| crossProjectOnly | bool | false | When true, returns only cross-project edges |
| includeContext | bool | false | Include surrounding code context (not yet implemented) |
| format | string | "json" | Output format: json, markdown, tron |
| offset | int | 0 | Result offset for pagination (default: return all) |
| limit | int | 0 | Result limit; 0 = return all (default), max 200 |

**`find_implementations`** — Find all types implementing an interface or deriving from a base class.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| symbolId | string | (required) | Stable SymbolId of the interface or base class |
| format | string | "json" | Output format: json, markdown, tron |

**`get_doc_coverage`** — Get documentation coverage metrics grouped by project, namespace, and symbol kind.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| project | string? | null | Project name filter (exact match). Omit for all projects. |
| format | string | "json" | Output format: json, markdown, tron |

**`diff_snapshots`** — Diff two snapshot versions showing added/removed/modified symbols.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| versionA | string | (required) | Snapshot A version (content hash or "latest") |
| versionB | string | (required) | Snapshot B version (content hash or "latest~1" for previous) |
| includeDiffs | bool | false | Include inline before/after doc content |
| format | string | "json" | Output format: json, markdown, tron |

**`explain_project`** — Get a comprehensive project overview in one call.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| chainedEntityDepth | int | 1 | Max depth for chained entity loading (0=summary only, 1=top-level types, 2+=children) |
| includeSections | string? | null | Sections to include (comma-separated): namespaces, types, stats, dependencies |
| excludeSections | string? | null | Sections to exclude (comma-separated) |
| format | string | "json" | Output format: json, markdown, tron |

#### Change Intelligence Tools (ChangeTools.cs)

**`review_changes`** — Review all changes between two snapshots, grouped by severity with unusual pattern detection.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| versionA | string | (required) | Before snapshot version (content hash) |
| versionB | string | (required) | After snapshot version (content hash) |
| verbose | bool | false | Include doc-only/trivial changes |
| format | string | "json" | Output format: json, markdown, tron |

**`find_breaking_changes`** — Find public API breaking changes between two snapshots. CI-optimized minimal output.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| versionA | string | (required) | Before snapshot version (content hash) |
| versionB | string | (required) | After snapshot version (content hash) |
| format | string | "json" | Output format: json, markdown, tron |

**`explain_change`** — Get a detailed human-readable explanation of changes to a specific symbol between two snapshots.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| versionA | string | (required) | Before snapshot version (content hash) |
| versionB | string | (required) | After snapshot version (content hash) |
| symbolId | string | (required) | SymbolId to explain |
| format | string | "json" | Output format: json, markdown, tron |

#### Ingestion Tools (IngestionTools.cs)

**`ingest_project`** — Ingest a .NET project or solution, building a queryable symbol graph.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| path | string | (required) | Absolute path to .sln, .slnx, or .csproj file |
| include | string? | null | Glob pattern to include projects (e.g. \*\*/\*.csproj) |
| exclude | string? | null | Glob pattern to exclude projects (e.g. \*\*/Tests/\*\*) |
| forceReindex | bool | false | Force re-index even if snapshot is current |

**`ingest_solution`** — Ingest an entire .NET solution (.sln), building a queryable symbol graph across all C# projects.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| path | string | (required) | Absolute path to .sln file |

#### Solution Tools (SolutionTools.cs)

**`explain_solution`** — Get a structured solution-level architecture overview: per-project stats, doc coverage, dependency DAG, stub counts.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| snapshotHash | string | (required) | Content hash of the solution snapshot (returned by ingest_solution) |

**`diff_solution_snapshots`** — Diff two solution snapshots: per-project symbol changes, projects added/removed, cross-project edge changes.
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| before | string | (required) | Before snapshot version (content hash) |
| after | string | (required) | After snapshot version (content hash) |

**Format options:** All query and change intelligence tools accept a `format` parameter with values `json` (default), `markdown`, or `tron`. Ingestion and solution tools return JSON only.

**Project filtering:** `search_symbols` and `get_doc_coverage` accept an optional `project` parameter for exact-match, case-sensitive project name filtering. Other tools operate on the full snapshot.

**Pagination:** `search_symbols` supports `offset`/`limit` (max 100, default 20). `get_references` supports `offset`/`limit` (max 200, default 0 = return all). Both return `totalCount` in the response envelope.

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
