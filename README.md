# DocAgentFramework

A polyglot .NET 10 / C# solution for turning **code documentation + code structure** into **agent-consumable memory**, exposed via a **securable MCP server**. Supports both **.NET (C#)** and **TypeScript (TS/TSX)** codebases.

DocAgentFramework ingests XML documentation + Roslyn symbol info for .NET, and utilizes a Node.js sidecar with the TypeScript Compiler API for TS/TSX. It normalizes this data into a stable "symbol graph" and a searchable BM25 index, serving it as a suite of 15 tools over the Model Context Protocol (MCP).

## Key Features

- **Polyglot Support**: Full compiler-grade symbol extraction for .NET and TypeScript projects.
- **Stable Symbol IDs**: Deterministic IDs that remain consistent across refactors and re-ingestions.
- **Incremental Ingestion**: SHA-256 based file hashing ensures only changed files are re-processed, with sub-100ms cache hits.
- **BM25 Search Index**: High-performance full-text search optimized for both `PascalCase` (.NET) and `camelCase` (TypeScript) naming conventions.
- **15 MCP Tools**: Comprehensive suite for searching, retrieving, referencing, and reviewing code changes.
- **Security First**: Strict `PathAllowlist` enforcement, comprehensive audit logging, and process isolation.

## MCP Tools

| # | Tool | Description |
|---|------|-------------|
| 1 | `search_symbols` | Search the symbol index using natural language or keywords |
| 2 | `get_symbol` | Get full details (members, docs, span) for a specific symbol ID |
| 3 | `get_references` | Find all usages of a symbol across projects |
| 4 | `find_implementations` | Locate implementations of interfaces or derived classes |
| 5 | `get_doc_coverage` | Audit documentation completion for a project or namespace |
| 6 | `diff_snapshots` | Compare two symbol graph versions to identify semantic changes |
| 7 | `review_changes` | High-level change analysis for PRs or local edits |
| 8 | `explain_project` | Generate a structural and functional overview of a project |
| 9 | `ingest_project` | Ingest a .NET project or solution |
| 10 | `ingest_typescript` | Ingest a TypeScript project (via `tsconfig.json`) |
| 11 | `ingest_solution` | Ingest an entire .NET solution |
| 12 | `explain_solution` | High-level overview of a full solution structure |
| 13 | `diff_solution_snapshots` | Semantic diffing at the solution level |
| 14 | `find_breaking_changes` | Detect API-breaking changes between versions |
| 15 | `explain_change` | Targeted explanation of specific code modifications |

## Prerequisites

- **.NET 10 SDK** — [download](https://dotnet.microsoft.com/download)
- **Node.js >= 22.0.0** — required only for TypeScript support

---

## Installation & Usage

### Option A: No install — `dnx` (recommended)

.NET 10 includes `dnx`, which runs NuGet tools on-demand without installing them (like `npx` for Node.js). **No install step required** — just configure your MCP host and go:

```bash
# Run DocAgent as HTTP server (default port 11877)
dnx DocAgent.McpServer

# Run on a custom port
dnx DocAgent.McpServer --port 3001

# Run in stdio mode (for CLI agent hosts)
dnx DocAgent.McpServer --stdio
```

Pin to a specific version:

```bash
dnx DocAgent.McpServer@2.2.0
```

### Option B: Global tool install

If you prefer a permanent install:

```bash
dotnet tool install -g DocAgent.McpServer
```

This puts `docagent` on your PATH. Update with:

```bash
dotnet tool update -g DocAgent.McpServer
```

### Option C: Run from source

For contributors, CI, or pinning to a specific commit:

```bash
git clone https://github.com/jamesburton/CSharpDocAgentFrameworkMCP.git
cd CSharpDocAgentFrameworkMCP

# HTTP mode (default port 11877)
dotnet run --project src/DocAgent.McpServer

# HTTP on custom port
dotnet run --project src/DocAgent.McpServer -- --port 3001

# stdio mode
dotnet run --project src/DocAgent.McpServer -- --stdio
```

---

## MCP Configuration

DocAgent supports two transports:

| Transport | Flag | When to use |
|-----------|------|-------------|
| **HTTP** (default) | `--port N` | Remote clients, browser-based agents, multi-client setups |
| **stdio** | `--stdio` | CLI agents (Claude Code, Cursor, VS Code), single-client |

The default HTTP port is **11877**. Override with `--port N` or `DOCAGENT_PORT` env var.

All examples below show the **`dnx` configuration first** (no install required), then alternatives for global tool and source-based setups.

### Claude Code CLI

Add to `~/.claude/settings.json` (or project-level `.claude/settings.json`):

**Using `dnx` (no install):**

```json
{
  "mcpServers": {
    "docagent": {
      "command": "dnx",
      "args": ["DocAgent.McpServer", "--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts",
        "DOCAGENT_ALLOWED_PATHS": "/path/to/your/projects/**"
      }
    }
  }
}
```

**Using global tool** (after `dotnet tool install -g DocAgent.McpServer`):

```json
{
  "mcpServers": {
    "docagent": {
      "command": "docagent",
      "args": ["--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts",
        "DOCAGENT_ALLOWED_PATHS": "/path/to/your/projects/**"
      }
    }
  }
}
```

**Using source** (no install, requires cloned repo):

```json
{
  "mcpServers": {
    "docagent": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/CSharpDocAgentFrameworkMCP/src/DocAgent.McpServer", "--", "--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/tmp/docagent-artifacts",
        "DOCAGENT_ALLOWED_PATHS": "/path/to/your/projects/**"
      }
    }
  }
}
```

### Claude Desktop

Edit `claude_desktop_config.json`:

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

```json
{
  "mcpServers": {
    "docagent": {
      "command": "dnx",
      "args": ["DocAgent.McpServer", "--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/Users/<user>/.docagent/artifacts",
        "DOCAGENT_ALLOWED_PATHS": "/Users/<user>/projects/**"
      }
    }
  }
}
```

Restart Claude Desktop after editing. Replace `dnx` with `docagent` (and adjust `args` to `["--stdio"]`) if using the global tool instead.

### VS Code / GitHub Copilot

Add to `.vscode/settings.json` (workspace) or user settings:

```json
{
  "github.copilot.chat.mcpServers": {
    "docagent": {
      "command": "dnx",
      "args": ["DocAgent.McpServer", "--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "${userHome}/.docagent/artifacts",
        "DOCAGENT_ALLOWED_PATHS": "${workspaceFolder}/**"
      }
    }
  }
}
```

### Cursor

Add to global config or per-project `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "docagent": {
      "command": "dnx",
      "args": ["DocAgent.McpServer", "--stdio"],
      "env": {
        "DOCAGENT_ARTIFACTS_DIR": "/home/<user>/.docagent/artifacts",
        "DOCAGENT_ALLOWED_PATHS": "/path/to/your/projects/**"
      }
    }
  }
}
```

### Other Agents

See [`docs/Agents.md`](docs/Agents.md) for Windsurf, OpenCode, and Zed configuration, plus instructions for adding support for new agent hosts.

---

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DOCAGENT_ARTIFACTS_DIR` | Directory for snapshot storage and search indices | `./artifacts` |
| `DOCAGENT_ALLOWED_PATHS` | Comma-separated glob patterns for permitted file access | Current working directory only |
| `DOCAGENT_PORT` | HTTP port for the MCP server (HTTP mode only) | `11877` |
| `DOCAGENT_INGESTION_TIMEOUT_SECONDS` | Max time for solution ingestion | `1800` (30 min) |
| `DOCAGENT_TELEMETRY_VERBOSE` | Enable verbose OpenTelemetry tracing (`true`/`false`) | `false` |

---

## Quick Usage

Once configured, use the tools from any connected AI agent:

```
# Ingest a .NET solution
ingest_solution path=/absolute/path/to/YourSolution.sln

# Search for symbols
search_symbols query="HttpClient" kindFilter="Type"

# Get full symbol details
get_symbol symbolId="MyApp.Services.AuthService"

# Find who implements an interface
find_implementations symbolId="MyApp.Core.IRepository"

# Check documentation coverage
get_doc_coverage project="MyApp.Core"

# Review changes between snapshots
review_changes versionA="abc123" versionB="def456"

# Solution-level overview
explain_solution snapshotHash="abc123"
```

---

## Security

DocAgent enforces a **default-deny** security model:

- **Path Allowlist**: Tools can only access files within `DOCAGENT_ALLOWED_PATHS` patterns. Everything else is denied with an opaque `not_found` response (no information leakage).
- **Audit Logging**: Every tool call is logged with parameters, outcome, and duration.
- **Process Isolation**: The TypeScript sidecar runs as a sandboxed child process with strict timeout and resource limits.
- **No Network in Tests**: All test fixtures are local — no implicit network calls.

---

## Building from Source

```bash
# Build
dotnet build src/DocAgentFramework.sln

# Run all tests (490 tests)
dotnet test src/DocAgentFramework.sln

# Run benchmarks (optional, requires Release config)
RUN_BENCHMARKS=1 dotnet test --filter "Category=Benchmark" -c Release

# Pack as NuGet tool
dotnet pack src/DocAgent.McpServer -c Release -o ./nupkg
```

---

## Documentation

| Document | Description |
|----------|-------------|
| [`docs/Architecture.md`](docs/Architecture.md) | Layer contracts, project dependencies, pipeline design |
| [`docs/Plan.md`](docs/Plan.md) | Implementation history (v1.0–v2.1) and future roadmap |
| [`docs/Testing.md`](docs/Testing.md) | Test strategy, categories, fixture patterns |
| [`docs/Security.md`](docs/Security.md) | MCP security model, threat mitigations |
| [`docs/Setup.md`](docs/Setup.md) | Detailed setup guide with hosting modes |
| [`docs/Agents.md`](docs/Agents.md) | Per-agent-host MCP configuration reference |
| [`docs/GitHooks.md`](docs/GitHooks.md) | Automatic re-ingestion via git hooks |

---

## License

MIT
