# Stack Research

**Domain:** .NET compiler-grade code documentation memory system with MCP server
**Researched:** 2026-02-26
**Confidence:** HIGH (core stack verified via NuGet, Context7, and official docs)

---

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET 10 | 10.0 (LTS) | Target framework | LTS release (shipped Nov 2025). `net10.0` + `LangVersion=preview` gives C# 14, file-scoped types, and primary constructors without preview risk. Already locked in project constraints. |
| C# 14 | (via .NET 10 SDK) | Language | Ships with .NET 10. Primary constructors, collection expressions, and `params` improvements reduce boilerplate in domain records. |
| `Microsoft.CodeAnalysis.CSharp` | **5.0.0** | Roslyn compiler APIs — walk symbols, parse XML doc IDs, resolve types and members | The definitive .NET compiler API. Nothing else provides semantic-level symbol resolution for C# with stability guarantees. 5.0.0 is the stable release shipping with VS 2026 / .NET 10. Current project pins 4.12.0 — **upgrade needed**. |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | **5.0.0** | MSBuild workspace loading for solution/project ingestion | Required companion to `Microsoft.CodeAnalysis.CSharp` for `MSBuildWorkspace`. Needed to load `.csproj` files without shelling out. Pin same version as core Roslyn package. |
| `ModelContextProtocol` | **1.0.0** | MCP server stdio transport, tool registration, protocol framing | Official C# SDK hit 1.0.0 on 2026-02-25 — the day before this research. No longer preview. Provides `AddMcpServer()`, `WithStdioServerTransport()`, `WithToolsFromAssembly()` DI extensions. Attribute-based tool registration (`[McpServerTool]`) is idiomatic. |
| Aspire | **13.1.0** | App host, service wiring, telemetry, dashboard, local orchestration | Renamed from ".NET Aspire" at v13 (Nov 2025). Version 13.1 adds MCP integration support. Provides `DistributedApplication`, OpenTelemetry auto-wiring, and the dashboard for observing tool calls during development. Supports .NET 10 explicitly. |

### Ingestion and Symbol Graph

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.CodeAnalysis.CSharp` | 5.0.0 | Walk `ISymbol` trees, resolve documentation comment IDs (e.g., `M:Foo.Bar.Method`), bind XML doc nodes to symbols | All ingestion paths. The `DocumentationCommentId` utility class on `ISymbol` generates the stable `SymbolId` string. |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 5.0.0 | Load `.sln`/`.csproj` via `MSBuildWorkspace`, get `SemanticModel` for each document | Required when loading real projects from disk. For test/fixture scenarios, use `AdhocWorkspace` instead (no MSBuild dependency). |
| `System.Xml.Linq` (inbox) | Framework | Parse XML documentation files (`.xml` from `GenerateDocumentationFile`) | Built into .NET — no NuGet package required. `XDocument.Load()` + LINQ-to-XML is sufficient for the structured `/// <summary>` format. Do not add an external XML parser. |
| `System.Text.Json` (inbox, v10.0.x) | Framework | Deterministic snapshot serialization to/from disk | Built into .NET 10. Source generators (`[JsonSerializable]`) produce fully AOT-compatible, allocation-minimal serializers. Use this for `SymbolGraphSnapshot` persistence. Do not use Newtonsoft.Json. |

### Search Index (BM25)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Lucene.Net` | **4.8.0-beta00017** | BM25 full-text search index over symbol graph nodes | The only mature, self-contained .NET BM25 implementation with tunable `k1`/`b` parameters (`Lucene.Net.Search.Similarities.BM25Similarity`). Still beta but has been in active use for years; the 4.8 series is a port of Java Lucene 4.8, which is battle-tested. Replaces `InMemorySearchIndex`. |
| `Lucene.Net.Analysis.Common` | **4.8.0-beta00017** | Standard tokenizer, English stemming, stop-word filters | Required companion for `StandardAnalyzer` and `EnglishAnalyzer`. Symbol names benefit from camelCase-aware tokenization. |
| `Lucene.Net.QueryParser` | **4.8.0-beta00017** | Parse structured query strings into `Query` objects | Needed if `search_symbols` MCP tool accepts Lucene-style query syntax (e.g., `name:Foo AND namespace:Bar`). Optional for V1; add when exposing structured search. |

> **Alternative path:** If Lucene.Net's perpetual-beta status is a concern, a hand-rolled BM25 implementation (the algorithm is ~50 LOC) backed by an in-memory inverted index is feasible for the symbol corpus sizes this project targets (tens of thousands of symbols). The `ISearchIndex` interface already abstracts this. Recommend starting with Lucene.Net for correctness, with the interface as the escape hatch.

### MCP Server and Hosting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `ModelContextProtocol` | 1.0.0 | MCP server (stdio transport), tool attribute registration, protocol message handling | Required. Use `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` pattern. Tools are plain static or instance methods annotated with `[McpServerTool]`. |
| `Microsoft.Extensions.Hosting` | 10.0.x (inbox) | Generic host, DI container, `IHostedService` lifecycle | Inbox with .NET 10. The MCP SDK integrates via `Host.CreateApplicationBuilder`. Do not reference the NuGet package explicitly unless you need a preview feature. |
| `Microsoft.Extensions.Logging` | 10.0.x (inbox) | Structured logging throughout all layers | Inbox. Every MCP tool call must log tool name, duration, and status (per architecture doc). Wire to stderr so stdout remains clean for MCP stdio protocol. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.x (inbox) | Service registration, `AddDocAgentCore()` extension pattern | Inbox. Use `IServiceCollection` extension methods for each layer (`AddDocAgentIngestion()`, `AddDocAgentMcpServer()`). |
| `Aspire.Hosting` | 13.1.0 | AppHost project, resource wiring, telemetry sinks, local dev dashboard | For the Aspire app host project. Provides `DistributedApplication.CreateBuilder()`, OpenTelemetry resource detection, and the Aspire Dashboard for observing tool calls. |

### Telemetry

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `OpenTelemetry` | **1.15.0** | Tracing, metrics, logs — all three signals stable | All layers. Aspire auto-wires OTLP exporter; add `AddOpenTelemetry()` in host startup. Use `ActivitySource` for spans around tool handler execution and ingestion steps. |
| `OpenTelemetry.Instrumentation.Http` | 1.x (stable) | HTTP client instrumentation (if remote git source added in V1.1+) | Add when `IProjectSource` gains a remote git implementation. Not needed for V1 file-system-only path. |

### Testing

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `xunit.v3` | **3.2.2** | Unit and integration test runner | Upgrade from xunit 2.7.1. v3 is the current stable (released 2026-01-14), supports .NET 8+, ships with better parallelism model and first-class `CancellationToken` support in test methods. |
| `FluentAssertions` | **8.8.0** | Readable assertion API | Upgrade from 6.12.1. v8 supports xunit.v3. Use `Should().Be()`, `Should().BeEquivalentTo()` patterns. Note: FA has a licensing change in v7+ — verify license acceptability before upgrade. |
| `Microsoft.CodeAnalysis.Testing` | 2.x | Roslyn analyzer unit test infrastructure | Add when implementing the public-API-change analyzers. Provides `AnalyzerTest<>` harnesses with in-memory compilation. Do not test analyzers against real project files in unit tests. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `dotnet-format` (SDK inbox) | Enforce `EditorConfig` style rules | Run as pre-commit check. Already `warnings-as-errors` in project — format violations become build breaks if wired to analyzer. |
| `Microsoft.CodeAnalysis.Analyzers` | Roslyn API correctness linting | Add as dev dependency when writing analyzers. Catches common Roslyn API misuse (e.g., calling `GetSymbolInfo` without a semantic model). |
| Aspire Dashboard | Local observability during development | Ships with Aspire AppHost. Visualizes tool call traces without a full OTLP backend. Free, no configuration. |

---

## Installation

```bash
# Upgrade Roslyn packages (from 4.12.0 to 5.0.0)
dotnet add package Microsoft.CodeAnalysis.CSharp --version 5.0.0
dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces --version 5.0.0

# MCP SDK (now stable 1.0.0 — remove the preview wildcard)
dotnet add package ModelContextProtocol --version 1.0.0

# Search index
dotnet add package Lucene.Net --version 4.8.0-beta00017
dotnet add package Lucene.Net.Analysis.Common --version 4.8.0-beta00017
dotnet add package Lucene.Net.QueryParser --version 4.8.0-beta00017

# Telemetry
dotnet add package OpenTelemetry --version 1.15.0

# Aspire AppHost (in the AppHost project)
dotnet add package Aspire.Hosting --version 13.1.0

# Testing (in test project)
dotnet add package xunit.v3 --version 3.2.2
dotnet add package FluentAssertions --version 8.8.0
```

> All packages go into `src/Directory.Packages.props` using `<PackageVersion>` entries (central package management already configured).

---

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| `Lucene.Net` 4.8.0-beta | Hand-rolled BM25 | If corpus stays under ~5,000 symbols and query syntax is simple keyword-only. Hand-rolled is deterministic, zero-dependency, easier to reason about. The `ISearchIndex` interface makes swapping trivial. |
| `Lucene.Net` 4.8.0-beta | `Azure.Search.Documents` (Azure AI Search) | If project moves to cloud deployment and budget exists for an Azure resource. Azure AI Search provides hosted BM25 + vector hybrid search. Not appropriate for local stdio MCP server targeting air-gapped use. |
| `System.Text.Json` (inbox) | `Newtonsoft.Json` | Never for this project. Newtonsoft is not AOT-compatible, adds a dependency, and is slower than `System.Text.Json` 10.x. |
| `xunit.v3` | `NUnit` / `MSTest` | If team has existing NUnit/MSTest investment. xunit.v3 is the idiomatic choice for new .NET 10 projects and is what Roslyn itself uses for its test suite. |
| `ModelContextProtocol` 1.0.0 | `mcpdotnet` | `mcpdotnet` is a community package predating the official SDK. Do not use — the official SDK is now stable and maintained in collaboration with Microsoft. |
| Aspire 13.x | Plain `IHostedService` | If Aspire introduces too much overhead for a simple stdio process. Aspire is optional for V1 but strongly recommended for telemetry wiring. The MCP server itself runs fine as a plain `Host.CreateApplicationBuilder()` process — Aspire wraps it for the orchestration layer. |

---

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `ModelContextProtocol` `0.0.0-preview.*` wildcard | The preview wildcard in the current `Directory.Packages.props` will resolve to pre-1.0 builds. The SDK hit 1.0.0 on 2026-02-25. Pin to `1.0.0` now — preview versions had breaking API changes across minor versions. | `ModelContextProtocol` 1.0.0 |
| `Microsoft.CodeAnalysis.CSharp` 4.12.0 | Misses C# 14 / .NET 10 language feature APIs. 5.0.0 is the stable release that ships with .NET 10 / VS 2026. Roslyn version must be >= the language version you're analyzing or `LangVersion=preview` features will be missing from semantic models. | 5.0.0 |
| `Newtonsoft.Json` | Not AOT-compatible (project targets `net10.0` with potential AOT path). Heavier than `System.Text.Json`. No advantage for this project's serialization needs. | `System.Text.Json` (inbox) |
| `FluentAssertions` 6.x | Current project pins 6.12.1, which does not support xunit.v3 properly (framework detection bug). Upgrade to 8.8.0. | `FluentAssertions` 8.8.0 |
| `xunit` 2.x | `xunit` 2.7.1 is the current pin. xunit v2 is in maintenance mode. v3 (3.2.2) is the current stable and the team has announced no new features for v2. Async test methods with `CancellationToken` are first-class in v3, which matters for the async ingestion and search paths. | `xunit.v3` 3.2.2 |
| Non-stdio MCP transports (HTTP, SSE) | Deferred to V2+ per project constraints. Stdio is the simplest security model — no network socket, no authentication complexity. Adding HTTP transport before the tool surface is stable would complicate the security posture. | Stdio transport only |
| Embeddings / vector index (V1) | Deferred per project constraints. The `IVectorIndex` interface is defined but not implemented in V1. Do not add `Microsoft.SemanticKernel` or any embedding provider yet — BM25 is the V1 search path. | Implement `IVectorIndex` in V2+ |
| `MSBuildLocator` without isolation | Loading MSBuild via `MSBuildWorkspace` in the same process as user code can cause assembly version conflicts. | Use `MSBuildLocator.RegisterDefaults()` before any Roslyn Workspace API call, or isolate workspace loading to a dedicated worker process. |

---

## Stack Patterns by Variant

**If building the Aspire AppHost project:**
- Reference `Aspire.Hosting` 13.1.0
- Add the MCP server project as `builder.AddProject<Projects.DocAgentMcpServer>("mcp-server")`
- Aspire auto-discovers OpenTelemetry endpoints and wires the dashboard

**If building the MCP server project (no Aspire):**
- Use `Host.CreateApplicationBuilder(args)` from `Microsoft.Extensions.Hosting`
- Add `builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`
- Redirect all logging to stderr: `consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace`
- This keeps stdout clean for MCP stdio protocol framing

**If writing Roslyn analyzers (diagnostic layer):**
- Add `Microsoft.CodeAnalysis.Analyzers` as a dev/build dependency
- Target `netstandard2.0` for analyzer projects (Roslyn analyzer host constraint — not `net10.0`)
- Use `Microsoft.CodeAnalysis.Testing` for test harnesses

**If loading real projects vs. fixtures:**
- Real projects: `MSBuildWorkspace.Create()` + `MSBuildLocator.RegisterDefaults()` — requires MSBuild SDK on PATH
- Test fixtures: `AdhocWorkspace` + `CSharpCompilation.Create()` — no MSBuild, fully deterministic, no network

---

## Version Compatibility

| Package | Compatible With | Notes |
|---------|-----------------|-------|
| `ModelContextProtocol` 1.0.0 | .NET 8+, .NET 10 | Verified: stable release targets `net8.0` baseline, runs on .NET 10. |
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | .NET Standard 2.0+ | Roslyn runs on netstandard2.0 shims but semantic analysis of C# 14 features requires 5.0.0. |
| `Lucene.Net` 4.8.0-beta00017 | .NET Standard 2.1, .NET 5+ | Confirmed .NET Standard 2.1 target. Runs on .NET 10 without issues. |
| `Aspire.Hosting` 13.1.0 | .NET 10 required | Aspire 13 requires .NET 10 SDK. AppHost project must target `net10.0`. |
| `xunit.v3` 3.2.2 | .NET 8+, .NET Framework 4.7.2+ | v3 dropped older netstandard targets. Test project must target `net10.0` or `net8.0`. |
| `FluentAssertions` 8.8.0 | `xunit.v3` 3.x | FA 8.x adds xunit.v3 framework detection. FA 6.x does not — this is the compatibility break from the current pin. |
| `OpenTelemetry` 1.15.0 | .NET 6+ | All three signals stable. Aspire 13 auto-configures OTLP exporter if `OTEL_EXPORTER_OTLP_ENDPOINT` is set. |

---

## Sources

- [NuGet: ModelContextProtocol 1.0.0](https://www.nuget.org/packages/ModelContextProtocol/) — stable release date 2026-02-25, HIGH confidence
- [Context7: modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) — `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` pattern verified, HIGH confidence
- [NuGet: Microsoft.CodeAnalysis.CSharp 5.0.0](https://www.nuget.org/packages/microsoft.codeanalysis.csharp/) — confirmed stable latest, HIGH confidence
- [NuGet: Lucene.Net 4.8.0-beta00017](https://www.nuget.org/packages/Lucene.Net/) — latest prerelease as of Oct 2024; BM25Similarity class documented in official Lucene.NET 4.8 API docs, MEDIUM confidence (prerelease, but mature)
- [Lucene.NET BM25Similarity API docs](https://lucenenet.apache.org/docs/4.8.0-beta00009/api/core/Lucene.Net.Search.Similarities.BM25Similarity.html) — BM25 support confirmed, HIGH confidence
- [Context7: Aspire.dev](https://github.com/microsoft/aspire.dev) — Aspire 13 requires .NET 10, single-file AppHost requires .NET 10 SDK, HIGH confidence
- [Aspire 13.1 MCP integration — InfoQ](https://www.infoq.com/news/2026/01/dotnet-aspire-13-1-release/) — Aspire 13.1 MCP integration noted, MEDIUM confidence (secondary source)
- [NuGet: OpenTelemetry 1.15.0](https://www.nuget.org/packages/OpenTelemetry) — confirmed stable, all three signals stable, HIGH confidence
- [xUnit.net: Core Framework v3 3.2.2](https://xunit.net/releases/v3/3.2.2) — released 2026-01-14, HIGH confidence
- [NuGet: FluentAssertions 8.8.0](https://www.nuget.org/packages/fluentassertions/) — latest stable, released 2025-10-23; xunit.v3 compatibility confirmed in GitHub issue #2935, HIGH confidence

---

*Stack research for: .NET compiler-grade code documentation memory system with MCP server*
*Researched: 2026-02-26*
