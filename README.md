# DocAgentFramework

A polyglot .NET 10 / C# solution for turning **code documentation + code structure** into **agent-consumable memory**, exposed via a **securable MCP server**. Supports both **.NET (C#)** and **TypeScript (TS/TSX)** codebases.

DocAgentFramework ingests XML documentation + Roslyn symbol info for .NET, and utilizes a Node.js sidecar with the TypeScript Compiler API for TS/TSX. It normalizes this data into a stable "symbol graph" and a searchable BM25 index, serving it as a suite of 15 tools over the Model Context Protocol (MCP).

## 🚀 Key Features

- **Polyglot Support**: Full compiler-grade symbol extraction for .NET and TypeScript projects.
- **Stable Symbol IDs**: Deterministic IDs that remain consistent across refactors and re-ingestions.
- **Incremental Ingestion**: SHA-256 based file hashing ensures only changed files are re-processed, with sub-100ms cache hits.
- **BM25 Search Index**: High-performance full-text search optimized for both `PascalCase` (.NET) and `camelCase` (TypeScript) naming conventions.
- **15 MCP Tools**: Comprehensive suite for searching, retrieving, referencing, and reviewing code changes.
- **Security First**: Strict `PathAllowlist` enforcement, comprehensive audit logging, and process isolation.

## 🛠️ MCP Tools

1.  `search_symbols`: Search the symbol index using natural language or keywords.
2.  `get_symbol`: Get full details (members, docs, span) for a specific symbol ID.
3.  `get_references`: Find all usages of a symbol across projects.
4.  `find_implementations`: Locate implementations of interfaces or derived classes.
5.  `get_doc_coverage`: Audit documentation completion for a project or namespace.
6.  `diff_snapshots`: Compare two symbol graph versions to identify semantic changes.
7.  `review_changes`: High-level change analysis for PRs or local edits.
8.  `explain_project`: Generate a structural and functional overview of a project.
9.  `ingest_project`: Ingest a .NET project or solution.
10. `ingest_typescript`: Ingest a TypeScript project (via `tsconfig.json`).
11. `ingest_solution`: Ingest an entire .NET solution.
12. `explain_solution`: High-level overview of a full solution structure.
13. `diff_solution_snapshots`: Semantic diffing at the solution level.
14. `find_breaking_changes`: Detect API-breaking changes between versions.
15. `explain_change`: Targeted explanation of specific code modifications.

## 📋 Quick Start

### Prerequisites
- .NET 10 SDK
- Node.js >= 22.0.0 (for TypeScript support)

### Installation
```bash
# Clone the repository
git clone https://github.com/your-org/DocAgentFramework.git
cd DocAgentFramework

# Build the project
dotnet build

# Running tests
dotnet test
```

### Running the Server
```bash
# Start MCP server locally (stdio transport)
dotnet run --project src/DocAgent.McpServer
```

## ⚙️ Configuration

DocAgent uses standard .NET configuration (`appsettings.json` or environment variables).

Key options in `DocAgentServerOptions`:
- `AllowedPaths`: List of directory patterns allowed for ingestion (default: repo root).
- `ArtifactsDir`: Path to store snapshots and search indices.
- `SidecarDir`: Path to the `ts-symbol-extractor` directory.
- `NodeExecutable`: Path to the Node.js binary (default: `node`).

## 🔒 Security

DocAgent is designed for secure agentic workflows:
- **Path Allowlist**: Tools only interact with files in configured directories.
- **Audit Logging**: Every tool execution is logged with parameters and outcomes.
- **Standard-compliant**: Follows MCP security best practices.

## 📄 License

MIT
