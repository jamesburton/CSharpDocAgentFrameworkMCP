# Project Research Summary

**Project:** CSharpDocAgentFrameworkMCP
**Domain:** .NET compiler-grade code documentation memory system with MCP server
**Researched:** 2026-02-26
**Confidence:** HIGH

## Executive Summary

This project builds an agent-native code documentation memory system for .NET/C# codebases, exposing compiler-accurate symbol intelligence through a Model Context Protocol (MCP) stdio server. The core thesis is that Roslyn's semantic model produces provably correct symbol information — types, nullability, generic constraints, member signatures — that no text parser or embedding heuristic can match. The recommended approach is a strict layered pipeline: ingest source via Roslyn workspace at build time, emit an immutable `SymbolGraphSnapshot` artifact, index it with BM25 for keyword search, and serve agent queries through a narrow MCP tool surface. The serving layer never holds a live Roslyn workspace — all queries are answered from the persisted snapshot.

The key competitive gap this fills is the combination of compiler accuracy, offline/self-hosted operation, and versioned diffable memory. Sourcegraph provides broad search but its symbol memory is opaque and requires a hosted service. CodeQL is a query engine, not a memory substrate. Raw LSP is live-only with no persistence or historical diff. DocAgentFramework occupies the intersection: deterministic, versionable, diff-capable, air-gap-compatible code memory for .NET agents.

The highest-risk decisions are made earliest: the `SymbolId` stability spec and snapshot determinism must be locked before any downstream work begins, because every diff, cache, and index depends on them. The most dangerous operational pitfall is stdout contamination breaking the MCP stdio framing — this must be enforced at server scaffold time. The recommended stack is fully confirmed: .NET 10, Roslyn 5.0.0, ModelContextProtocol 1.0.0 (just reached stable), Lucene.Net 4.8.0-beta for BM25, Aspire 13.1 for hosting, and xunit.v3 for testing.

## Key Findings

### Recommended Stack

The stack is high-confidence and well-grounded. .NET 10 (LTS) with C# 14 is the locked target. The most important upgrade is Roslyn from 4.12.0 to **5.0.0** — the current pin misses C# 14 semantic APIs. The MCP SDK just reached **1.0.0** (2026-02-25) and the preview wildcard in `Directory.Packages.props` must be pinned immediately. Lucene.Net 4.8.0-beta is the only mature self-contained BM25 implementation for .NET; its `ISearchIndex` abstraction allows swapping to a hand-rolled implementation if the beta status is unacceptable. All logging must route to stderr to protect the MCP stdio stream — this is non-negotiable.

**Core technologies:**
- `.NET 10 / C# 14`: Target framework — LTS, already locked in project constraints
- `Microsoft.CodeAnalysis.CSharp 5.0.0`: Roslyn compiler API — upgrade from 4.12.0 required for C# 14 semantic models
- `ModelContextProtocol 1.0.0`: MCP stdio server SDK — just reached stable; pin immediately, drop preview wildcard
- `Lucene.Net 4.8.0-beta00017`: BM25 full-text search — only mature .NET BM25 option; `ISearchIndex` interface is the escape hatch
- `Aspire 13.1.0`: App host + telemetry — optional for V1 but strongly recommended; routes OpenTelemetry without boilerplate
- `xunit.v3 3.2.2 + FluentAssertions 8.8.0`: Testing — upgrade from v2/6.x; xunit.v3 required for proper async CancellationToken support

### Expected Features

**Must have (table stakes — V1):**
- Stable `SymbolId` spec — foundation for everything; wrong decisions here force a full rebuild
- Roslyn symbol graph walker — namespaces, types, members, file spans; this is the compiler truth engine
- XML doc parser with symbol binding — binds prose documentation to specific symbols via XML doc ID round-trip
- Deterministic snapshot serialization — same input must produce identical bytes; required for diff and caching
- BM25 search index — solves 80% of agent navigation cheaply and deterministically
- MCP tools: `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project` — the agent-facing surface
- Path allowlist + audit logging — minimum security posture for CI use
- Snapshot artifact storage — write versioned snapshots to `artifacts/snapshots/` for historical diff

**Should have (competitive differentiators — V1.x / V2):**
- Semantic diff engine with risk scoring — structured API surface change detection; no competitor exposes this as a composable MCP tool
- Roslyn analyzers for doc parity enforcement — catch public API changes not reflected in docs at CI time
- Doc coverage policy enforcement as a build gate — configurable threshold per project/namespace
- PackageRefGraph — NuGet dependency graph reasoning; unavailable in LSP or Sourcegraph without custom indexing
- `review_changes` MCP tool (Unusual Change Review skill) — synthesizes semantic diff + analyzer findings

**Defer (V2+):**
- `IVectorIndex` implementation — define interface in V1 only; implement when BM25 proves insufficient
- HTTP/SSE MCP transport — adds auth complexity; defer until stdio is proven and auth model is designed
- Embeddings / semantic search — adds non-determinism and provider coupling; BM25 covers 80% of navigation needs
- Polyglot via Tree-sitter — locks to C#/Roslyn until generic `ISymbolGraphBuilder<TDoc>` contract is stable
- Query DSL — premature without known query patterns from real agent usage

### Architecture Approach

The architecture follows a strict compiler pipeline pattern: each phase produces an immutable artifact consumed by the next, and no downstream component can reach backward to an upstream one. The `SymbolGraphSnapshot` is the critical interchange artifact — versioned, deterministically serialized, and persisted to `artifacts/`. The MCP serving layer is deliberately thin: it validates input, delegates to `IKnowledgeQueryService`, formats output, and logs. All business logic lives in the facade and below. The Roslyn workspace is a build-time tool only; it is never held open in the serving path.

**Major components:**
1. `DocAgent.Core` — Pure domain: zero-dependency interfaces (`IProjectSource`, `IDocSource`, `ISymbolGraphBuilder`, etc.) and domain types (`SymbolNode`, `SymbolGraphSnapshot`, `SymbolId`, `GraphDiff`)
2. `DocAgent.Ingestion` — All I/O-touching, source-format-specific code: `LocalProjectSource`, `LocalDocSource`, `XmlDocParser`, `RoslynSymbolGraphBuilder`
3. `DocAgent.Indexing` — BM25 `ISearchIndex` + `SnapshotStore`; decoupled from ingestion, consumes only `SymbolGraphSnapshot` values
4. `DocAgent.Analysis` — `SymbolDiffEngine` + Roslyn `DiagnosticAnalyzer` implementations; pure functions over snapshots
5. `DocAgent.McpServer` — Thin MCP tool handlers, path allowlist, audit logger; security boundary lives here, not deeper
6. `DocAgent.AppHost` — Aspire app host, DI wiring, OpenTelemetry configuration

### Critical Pitfalls

1. **Symbol identity instability** — Using `ISymbol.ToDisplayString()` or raw XML doc IDs as persisted `SymbolId` values breaks on rename; add `PreviousIds` rename tracking, write a golden-file rename test, and lock the `SymbolId` spec before any other work. Address: Phase 1 (Core Domain).

2. **Stdout contamination breaking MCP protocol** — Any `Console.Write`, unhandled exception trace, or logger writing to stdout corrupts MCP JSON-RPC framing. Redirect all `ILogger` sinks to stderr from day one; add an integration test that captures raw stdout bytes and asserts valid JSON-RPC only. Address: Phase 5 (MCP Server scaffold).

3. **MSBuildWorkspace memory leakage** — Roslyn `Compilation` objects cannot be GC'd without an `AssemblyLoadContext` boundary; repeated builds in a long-running process cause unbounded memory growth. Isolate each snapshot build in a collectible `AssemblyLoadContext` or run ingestion as a short-lived worker process. Address: Phase 2 (Ingestion pipeline).

4. **Non-deterministic snapshot serialization** — `HashSet<T>`, `Dictionary<K,V>`, and unordered Roslyn symbol enumeration produce different orderings across runs. Sort all collections before serialization; write a byte-identical cross-run determinism test as the first snapshot test. Address: Phase 1 + Phase 2.

5. **XML doc binding failures on generics and partial types** — Backtick-encoded XML doc IDs (`M:Foo.Bar``1(System.String,``0)`) have known edge cases for generics, partial classes, overloads, and operators. Use `DocumentationCommentId.GetFirstSymbolForDeclarationId()` for round-trip lookup; track unbound doc entries as a pipeline metric. Address: Phase 2 (Ingestion pipeline).

6. **Prompt injection via doc comments** — XML doc comment text read verbatim by agents is an injection vector (OWASP LLM01:2025). Implement output redaction hooks; return structured DTOs rather than raw strings; mark tool output as "data, not instructions." Address: Phase 5 (Security hardening).

## Implications for Roadmap

Based on research, the architecture's own build-order analysis maps directly to phases. Each phase is independently testable before the next phase begins.

### Phase 1: Core Domain Contracts
**Rationale:** Everything else depends on `DocAgent.Core`. The `SymbolId` spec and snapshot schema must be locked first — wrong decisions here force a full rebuild of every downstream component. This is the highest-leverage, lowest-implementation-cost phase.
**Delivers:** Locked `SymbolId` spec, `SymbolGraphSnapshot` schema with schema version + content hash, all interface contracts, domain records. Zero runtime dependencies.
**Addresses:** Stable symbol identity (P1 feature), snapshot versioning and schema migration (P1 feature)
**Avoids:** Symbol identity instability (Pitfall 1), non-deterministic serialization (Pitfall 4)
**Research flag:** Standard patterns — no additional research needed; Roslyn XML doc ID format is well-documented.

### Phase 2: Ingestion Pipeline
**Rationale:** The index consumes snapshots; you need a real snapshot from a real codebase to validate the index. Ingestion must produce a correct, deterministic snapshot before indexing begins.
**Delivers:** `LocalProjectSource`, `LocalDocSource`, `XmlDocParser` (with generic/partial/overload edge cases), `RoslynSymbolGraphBuilder` (Roslyn walk + doc merge), `SnapshotStore` (artifacts/ read/write), determinism test suite.
**Uses:** `Microsoft.CodeAnalysis.CSharp 5.0.0`, `Microsoft.CodeAnalysis.CSharp.Workspaces 5.0.0`, `System.Xml.Linq` (inbox), `System.Text.Json` (inbox)
**Avoids:** MSBuildWorkspace memory leak (Pitfall 3), XML doc binding failures (Pitfall 5), non-deterministic serialization (Pitfall 4)
**Research flag:** May need deeper research on `MSBuildLocator` isolation strategies and `AssemblyLoadContext` boundary patterns for snapshot build lifecycle.

### Phase 3: BM25 Search Index
**Rationale:** The query facade delegates to the index; the facade must be built on a real index, not the `InMemorySearchIndex` stub. BM25 replaces the stub with a correctness-validated implementation.
**Delivers:** `Bm25SearchIndex` replacing `InMemorySearchIndex`, camelCase-aware tokenization, index persistence alongside snapshots, `IVectorIndex` interface (stub only).
**Uses:** `Lucene.Net 4.8.0-beta00017`, `Lucene.Net.Analysis.Common`, `Lucene.Net.QueryParser`
**Avoids:** BM25 index rebuilt from scratch on every search (performance trap), stub fallback silently used in production
**Research flag:** Standard patterns — Lucene.Net BM25Similarity is well-documented; no additional research needed.

### Phase 4: Query Facade
**Rationale:** MCP tools must be thin wrappers around a tested facade. The facade isolates business logic (search ranking, diff, filtering) from the transport layer, enabling testing without a running MCP server.
**Delivers:** `IKnowledgeQueryService` wired to `ISearchIndex` + `SnapshotStore`; `SearchAsync`, `GetSymbolAsync`, `DiffAsync` (basic structural diff).
**Implements:** Query facade pattern (Architecture Pattern 3)
**Avoids:** Thin core / fat tools anti-pattern (Architecture Anti-Pattern 3)
**Research flag:** Standard patterns — facade pattern is well-established.

### Phase 5: MCP Server + Security
**Rationale:** The serving boundary is where security lives. Stdio contamination, path traversal, prompt injection, and scope creep must all be addressed at tool registration time before any tools ship.
**Delivers:** `DocTools.cs` handlers for `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`; `PathAllowlist`; `AuditLogger`; stderr-only logging configuration; integration test for raw stdout byte capture.
**Uses:** `ModelContextProtocol 1.0.0` (stable — pin immediately), `Microsoft.Extensions.Hosting` (inbox)
**Avoids:** Stdout contamination (Pitfall 2), MCP tool scope creep (Pitfall 6), prompt injection (Pitfall 7), path traversal (security mistake)
**Research flag:** Needs research — MCP SDK 1.0.0 was released the day before this research. Verify exact `[McpServerTool]` attribute API, tool schema validation via `mcp inspect`, and stdio transport configuration. Also verify Aspire 13.1 + MCP integration patterns.

### Phase 6: Analysis Layer
**Rationale:** Diff and analyzer logic are additive features on top of a working pipeline. They depend on a stable snapshot format and validated ingestion pipeline, both completed in earlier phases.
**Delivers:** `SymbolDiffEngine` (full semantic diff with risk scoring, nullability classification, compiler-synthesized member exclusion), Roslyn `DiagnosticAnalyzer` implementations for doc parity and suspicious edit detection, `review_changes` MCP tool.
**Avoids:** Diff false positives from nullability and compiler-generated members (Pitfall 8)
**Research flag:** Needs research — risk classification model for semantic diff findings (nullability changes as LOW risk, binary-incompatible changes as HIGH) benefits from review of APIDiff literature and Roslyn `ISymbol.IsImplicitlyDeclared` API.

### Phase 7: Host + Observability
**Rationale:** Aspire wiring is straightforward once the components it orchestrates exist. Telemetry is the last thing to add because it depends on all layers being stable.
**Delivers:** `DocAgent.AppHost`, DI extension methods (`AddDocAgentCore`, `AddDocAgentIngestion`, `AddDocAgentMcpServer`), OpenTelemetry wiring, Aspire dashboard for tool call observation.
**Uses:** `Aspire.Hosting 13.1.0`, `OpenTelemetry 1.15.0`
**Research flag:** Standard patterns — Aspire DI wiring and OpenTelemetry are well-documented.

### Phase 8: V1.x Enhancements (Post-Validation)
**Rationale:** These features add significant value but depend on the core pipeline being validated by real agent workloads first.
**Delivers:** `PackageRefGraph` (NuGet dependency reasoning), semantic diff risk scoring calibration, Roslyn analyzer doc coverage policy gate (configurable threshold per namespace), snapshot catalog.
**Research flag:** Standard patterns for PackageRefGraph (parses `.csproj`, `Directory.Packages.props`). Coverage policy gate needs threshold calibration research.

### Phase Ordering Rationale

- Core Domain must precede everything: the `SymbolId` spec is a dependency of every downstream artifact. Getting it wrong forces a full rebuild.
- Ingestion precedes Indexing: the index consumes `SymbolGraphSnapshot` values; no real snapshot = no real index validation.
- Indexing precedes Query Facade: the facade delegates to the index; stub tests are insufficient for integration confidence.
- Query Facade precedes MCP Tools: business logic must be in the facade before tools are built to prevent the fat-tools anti-pattern.
- Analysis after Serving: diff and analyzers are additive features; they must not block the working read-only pipeline.
- Host last: wiring is trivial once the components it wires exist.

### Research Flags

Phases needing deeper research during planning:
- **Phase 2 (Ingestion):** `AssemblyLoadContext` isolation strategy for Roslyn workspace memory management; `MSBuildLocator.RegisterDefaults()` process isolation patterns.
- **Phase 5 (MCP Server):** MCP SDK 1.0.0 is brand new (released 2026-02-25); verify exact attribute API (`[McpServerTool]`), tool schema validation, and Aspire 13.1 MCP integration patterns.
- **Phase 6 (Analysis):** Semantic diff risk classification model; `ISymbol.IsImplicitlyDeclared` API for excluding compiler-synthesized members.

Phases with standard, well-documented patterns (skip research-phase):
- **Phase 1 (Core Domain):** Roslyn XML doc ID format is officially documented; domain record design is straightforward.
- **Phase 3 (BM25 Index):** Lucene.Net BM25Similarity and StandardAnalyzer patterns are well-documented.
- **Phase 4 (Query Facade):** Facade pattern over search index is well-established.
- **Phase 7 (Host):** Aspire + OpenTelemetry wiring follows official docs directly.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Core packages verified via NuGet. ModelContextProtocol 1.0.0 stable release confirmed 2026-02-25. Roslyn 5.0.0 confirmed stable with .NET 10. Lucene.Net perpetual-beta status is the only uncertainty. |
| Features | HIGH (P1) / MEDIUM (P2+) | V1 table stakes derived from existing project plans and validated against competitor analysis. Differentiator features (semantic diff, review_changes) based on project plans; competitive analysis via WebSearch is MEDIUM confidence. |
| Architecture | HIGH | Grounded in existing `DocAgent.Core` contracts, Roslyn SDK official docs, and LSIF specification. Build order and anti-patterns verified against Roslyn GitHub issues. |
| Pitfalls | HIGH (Roslyn) / MEDIUM (MCP) | Roslyn symbol stability, memory, and XML doc binding pitfalls verified against GitHub issues and official API docs. MCP pitfalls verified via Microsoft DevBlog and Nearform guide. Prompt injection risk class-1 on OWASP LLM Top 10 2025. |

**Overall confidence:** HIGH

### Gaps to Address

- **Lucene.Net beta status:** The 4.8.0-beta series is mature in practice but carries formal prerelease designation. If this becomes a concern during planning, evaluate the hand-rolled BM25 alternative (the `ISearchIndex` interface makes swapping trivial). Resolve at Phase 3 kickoff.
- **MCP SDK 1.0.0 API surface:** The SDK was released the day before this research. The `[McpServerTool]` attribute registration pattern is confirmed, but edge cases in schema validation and error handling should be verified via `mcp inspect` tooling at Phase 5 start.
- **FluentAssertions license change:** v7+ has a licensing change from Apache 2.0. Verify license acceptability for this project before Phase 1 testing begins. If unacceptable, Shouldly or raw xunit assertions are viable alternatives.
- **Aspire 13.1 MCP integration details:** InfoQ reference is a secondary source. Verify the exact `AddProject` + MCP wiring API before Phase 7 implementation.
- **`inheritdoc` expansion:** XML doc `<inheritdoc/>` tags are not expanded by the compiler XML output. The ingestion pipeline must expand these during `XmlDocParser` processing using the symbol hierarchy. This is a non-trivial edge case not fully addressed in Phase 2 scope above; it should be explicitly included as a tracked task.

## Sources

### Primary (HIGH confidence)
- [NuGet: ModelContextProtocol 1.0.0](https://www.nuget.org/packages/ModelContextProtocol/) — stable release date, API surface
- [Context7: modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) — `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` pattern
- [NuGet: Microsoft.CodeAnalysis.CSharp 5.0.0](https://www.nuget.org/packages/microsoft.codeanalysis.csharp/) — version compatibility with .NET 10
- [.NET Compiler Platform SDK concepts — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/compiler-api-model) — Roslyn architecture
- [LSIF Specification 0.4.0 — Microsoft](https://microsoft.github.io/language-server-protocol/specifications/lsif/0.4.0/specification/) — immutable snapshot pattern
- [ISymbol Interface — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.isymbol) — symbol equality and stability
- [xUnit.net Core Framework v3 3.2.2](https://xunit.net/releases/v3/3.2.2) — testing framework
- [NuGet: FluentAssertions 8.8.0](https://www.nuget.org/packages/fluentassertions/) — xunit.v3 compatibility
- [NuGet: OpenTelemetry 1.15.0](https://www.nuget.org/packages/OpenTelemetry) — stable signals
- [Build an MCP server in C# — .NET Blog](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/) — C# SDK guidance
- [LLM01:2025 Prompt Injection — OWASP](https://genai.owasp.org/llmrisk/llm01-prompt-injection/) — security threat classification
- Project source contracts: `src/DocAgent.Core/Abstractions.cs`, `src/DocAgent.Core/Symbols.cs`, `docs/Plan.md`, `EXTENDED_PLANS/`

### Secondary (MEDIUM confidence)
- [Lucene.NET BM25Similarity API docs](https://lucenenet.apache.org/docs/4.8.0-beta00009/api/core/Lucene.Net.Search.Similarities.BM25Similarity.html) — BM25 support
- [Aspire 13.1 MCP integration — InfoQ](https://www.infoq.com/news/2026/01/dotnet-aspire-13-1-release/) — Aspire + MCP integration
- [Implementing MCP: Tips, Tricks and Pitfalls — Nearform](https://nearform.com/digital-community/implementing-model-context-protocol-mcp-tips-tricks-and-pitfalls/) — stdio contamination, tool design
- [New Prompt Injection Attack Vectors Through MCP — Palo Alto Unit 42](https://unit42.paloaltonetworks.com/model-context-protocol-attack-vectors/) — MCP-specific injection threat model
- [Evaluating the impact of LSP-based code intelligence on coding agents](https://www.nuanced.dev/blog/evaluating-lsp) — LSP vs text-search latency data
- [Memory leak on Microsoft.CodeAnalysis.Scripting — dotnet/roslyn #41348](https://github.com/dotnet/roslyn/issues/41348) — compilation memory retention
- [APIDiff: Detecting API breaking changes — Semantic Scholar](https://www.semanticscholar.org/paper/APIDiff:-Detecting-API-breaking-changes-Brito-Xavier/a02f93289afe58989589abafc6ae098ef8e544a8) — semantic diff false positive research

### Tertiary (LOW confidence)
- [CodeQL LSP MCP Server](https://lobehub.com/mcp/neuralprogram-codeql-lsp-mcp) — competitor feature analysis; third-party listing
- [Sourcegraph Amp Agent](https://www.amplifilabs.com/post/sourcegraph-amp-agent-accelerating-code-intelligence-for-ai-driven-development) — competitive landscape; Sourcegraph's internal index schema is not public

---
*Research completed: 2026-02-26*
*Ready for roadmap: yes*
