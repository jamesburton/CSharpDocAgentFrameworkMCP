---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: unknown
last_updated: "2026-02-27T13:27:59.468Z"
progress:
  total_phases: 6
  completed_phases: 5
  total_plans: 18
  completed_plans: 17
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-25)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** Phase 7 — Runtime Integration Wiring

## Current Position

Phase: 7 of 7 (Runtime Integration Wiring) — COMPLETE
Plan: 3 of 3 in phase 7 (07-03 complete)
Status: 07-03 complete — E2E integration tests proving full pipeline through real DI container; 6 integration tests pass; 123 total tests passing
Last activity: 2026-02-27 — Completed 07-03 (2 files, 1 task, 123 tests passing)

Progress: [██████████] 100% (18/18 plans complete across all phases)

## Performance Metrics

**Velocity:**
- Total plans completed: 3
- Average duration: 18 min
- Total execution time: 0.9 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| Phase 1 - Core Domain | 3/3 | 54 min | 18 min |

**Recent Trend:**
- Last 5 plans: 01-01 (16m), 01-02 (10m), 01-03 (8m)
- Trend: accelerating

*Updated after each plan completion*
| Phase 01-core-domain P02 | 364 | 2 tasks | 7 files |
| Phase 02-ingestion-pipeline P02 | 10 | 2 tasks | 5 files |
| Phase 03-bm25-search-index P01 | 45 | 2 tasks | 5 files |
| Phase 03-bm25-search-index P02 | 24 | 2 tasks | 2 files |
| Phase 04-query-facade P01 | 35 | 2 tasks | 6 files |
| Phase 04-query-facade P02 | 15 | 1 tasks | 2 files |
| Phase 05-mcp-server-security P01 | 52 | 2 tasks | 10 files |
| Phase 05-mcp-server-security P03 | 32 | 2 tasks | 3 files |
| Phase 05-mcp-server-security P02 | 23 | 2 tasks | 6 files |
| Phase 07-runtime-integration-wiring P02 | 7 | 2 tasks | 3 files |
| Phase 07-runtime-integration-wiring P03 | 12 | 1 tasks | 2 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Pre-phase]: Non-generic `ISymbolGraphBuilder` for V1 — simpler V1 contract
- [Pre-phase]: Stdio-only MCP transport for V1 — simplest security model
- [Pre-phase]: BM25 first via Lucene.Net, embeddings behind `IVectorIndex` interface only
- [Pre-phase]: Snapshot artifacts to `artifacts/` directory, file-based storage for V1
- [01-01]: PreviousIds as IReadOnlyList on SymbolNode (not a dedicated edge) for V1 simplicity
- [01-01]: Root Directory.Packages.props created — tests/ is outside src/ tree; CPM must be at common ancestor
- [01-01]: ContentHash on SymbolGraphSnapshot is nullable — set by persistence layer to avoid circular dependency
- [01-01]: Verify.Xunit 31.x requires xunit 2.9.x (upgraded from 2.7.1); Microsoft.NET.Test.Sdk 18.0.1 required for .NET 10 SDK testhost
- [01-01]: [UseVerify] in v31 is an assembly-level build attribute, not a class attribute — no per-class decoration needed
- [Phase 01-02]: ContractlessStandardResolver used for MessagePack so domain types need no serialization attributes
- [Phase 01-02]: System.IO.Hashing 9.0.0 required as explicit package (not auto-included in .NET 10)
- [01-03]: [EnumeratorCancellation] attribute not valid on interface declarations — only on async-iterator method implementations
- [01-03]: IVectorIndex left as empty stub (VCTR-01) — V2 concern, embeddings out of scope for V1
- [02-01]: Action<string> logWarning used instead of ILogger to keep Ingestion free of Microsoft.Extensions.Logging dependency
- [02-01]: NuGetAuditMode=direct on Ingestion and Tests projects to suppress transitive Microsoft.Build.Tasks.Core 17.7.2 vulnerability advisory (MSBuildWorkspace 4.12 transitive dep)
- [Phase 02-ingestion-pipeline]: XmlDocParser uses per-symbol API (Parse(string?)) aligning with Roslyn ISymbol.GetDocumentationCommentXml() call pattern
- [Phase 02-ingestion-pipeline]: Malformed XML recovery: wrap in <doc> first, then fall back to raw text with [Parse warning] prefix
- [02-03]: CoreSymbolKind alias required — DocAgent.Core.SymbolKind conflicts with Microsoft.CodeAnalysis.SymbolKind in Ingestion namespace
- [02-03]: MSBuildWorkspace created and disposed per project inside ProcessProjectAsync to bound Roslyn compilation memory
- [02-03]: Accessibility.ProtectedOrInternal mapped to ProtectedInternal and included in accessibility filter
- [02-04]: ContentHash computed over bytes with ContentHash=null to avoid circular dependency; final file stored with hash set
- [02-04]: Atomic manifest update via temp file + File.Move(overwrite:true); duplicate hashes replace existing manifest entries
- [02-05]: Fix CreatedAt via with-expression after BuildAsync returns rather than modifying builder API — keeps ISymbolGraphBuilder interface minimal
- [Phase 03-bm25-search-index]: Custom CamelCaseTokenizer (regex-based) instead of WordDelimiterFilter — beta00017 doesn't split XMLParser-style acronym boundaries
- [Phase 03-bm25-search-index]: BM25 k1=2.0, b=0.5 for symbolName field for better symbol-name ranking
- [Phase 03-02]: Two-commit protocol required for SetCommitData: first commit flushes documents, second commit stores snapshotHash in Lucene metadata
- [Phase 03-02]: LoadIndexAsync throws DirectoryNotFoundException (missing) or InvalidOperationException (stale) for clear caller error handling
- [Phase 04-query-facade]: KnowledgeQueryService resolves snapshots by IngestedAt sort; DiffAsync/GetReferencesAsync stubbed for Phase 5/6; NuGetAuditMode=direct added to DocAgent.Indexing for transitive advisory suppression
- [Phase 04-query-facade]: ResponseEnvelope SnapshotVersion uses snapshot B ContentHash on DiffAsync
- [05-01]: MCP 1.0.0 breaking change — [McpTool]/[McpToolMethod] replaced by [McpServerToolType]/[McpServerTool]
- [05-01]: AddCallToolFilter via WithRequestFilters on IMcpRequestFilterBuilder (not directly on IMcpServerBuilder)
- [05-01]: CallToolResult + TextContentBlock in ModelContextProtocol.Protocol namespace; RequestContext<T>.Services via MessageContext inheritance
- [05-01]: SymbolId has no Parse method — construct via new SymbolId(string); Arguments in CallToolRequestParams are IReadOnlyDictionary<string, JsonElement>
- [05-01]: Research flag RESOLVED — MCP SDK 1.0.0 [McpServerTool] API verified and implemented
- [Phase 05-03]: Integration tests marked [Trait Category Integration] for CI filter separation — subprocess-spawning tests must run separately from unit tests
- [Phase 05-03]: Path denial behaviour is spansRedacted=true with span=null (not error response) — tests match actual DocTools implementation from 05-01
- [Phase 05-02]: PathAllowlist.MatchesAny fixed: FileSystemGlobbing Matcher.Match(string) returns false for absolute paths — must strip path root and use Match(root, relativePath)
- [07-01]: AddDocAgent() uses closure-based GetDir() to prevent SnapshotStore/BM25SearchIndex path divergence
- [07-01]: DOCAGENT_ARTIFACTS_DIR env var injected into IConfiguration before Configure<DocAgentServerOptions>() to ensure env var precedence
- [07-01]: Startup uses IndexAsync (idempotent BM25 freshness check) rather than LoadIndexAsync for warm-up
- [Phase 07-02]: GetReferencesAsync returns ALL edge types bidirectionally; SymbolNotFoundException thrown before first yield; DocTools maps to NotFound error response
- [07-03]: DocAgentServerOptions init->set required — init accessors cannot be assigned in services.Configure<T>() lambdas (IOptions pattern)

### Pending Todos

None.

### Blockers/Concerns

- [Research flag] Phase 2: MSBuildLocator isolation and AssemblyLoadContext boundary strategy needs confirmation during planning
- [RESOLVED] Phase 5: MCP SDK 1.0.0 [McpServerTool] attribute API verified and implemented in 05-01
- [Research flag] Phase 6: Semantic diff risk classification model for Analysis layer
- [Dependency] Roslyn version: current pin is 4.12.0, research recommends upgrade to 5.0.0 for C# 14 semantic APIs — confirm in Phase 2 plan
- [RESOLVED] FluentAssertions v7+ license change — kept at 6.12.1 (Apache 2.0) in Directory.Packages.props

## Session Continuity

Last session: 2026-02-27
Stopped at: Completed 07-03-PLAN.md — E2E integration tests proving full pipeline through real DI container; DocAgentServerOptions init->set fix; 6 E2E tests + 123 total tests passing. Phase 7 COMPLETE.
Resume file: None
