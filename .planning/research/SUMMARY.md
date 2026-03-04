# Project Research Summary

**Project:** DocAgentFramework v1.5 Robustness
**Domain:** .NET 10 MCP server — compiler-grade symbol graph with Roslyn ingestion pipeline
**Researched:** 2026-03-04
**Confidence:** HIGH

## Executive Summary

DocAgentFramework v1.5 is a targeted robustness milestone on an already-shipped MCP server. The 12-tool surface exists; this milestone adds two new tools (`find_implementations`, `get_doc_coverage`), extends pagination consistency to `get_references`, hardens the server with rate limiting and startup config validation, optimises the hot query path from O(E) edge scans to O(1) dictionary lookups, and brings Roslyn from 4.12.0 to 4.14.0. All research was conducted against the live codebase — findings are high-confidence because they are grounded in direct source reading, not speculation about a greenfield design.

The recommended execution order is dependency-driven: Roslyn 4.14 upgrade first (eliminates the existing VersionOverride hack and resolves the latent NU1107 conflict introduced by BenchmarkDotNet's transitive Roslyn 4.14 pull), then Core contract extensions (three new methods on `IKnowledgeQueryService`), then parallel tracks for Indexing implementation and McpServer infrastructure, then tool surface additions, finishing with the CLAUDE.md documentation refresh. This ordering avoids building features on a misaligned dependency graph and ensures the O(1) edge index is in place before `find_implementations` relies on it.

The dominant risks are version alignment (the Roslyn package family must all move together or NU1107 returns), determinism (introducing `Dictionary` for O(1) lookup must not remove the `List` used for serialisation ordering), and pagination backward-compatibility (default behavior must not silently truncate existing callers). All six critical pitfalls have clear, actionable prevention strategies and are mapped to specific phases.

---

## Key Findings

### Recommended Stack

No new runtime packages are required for v1.5. `System.Threading.RateLimiting` ships in the .NET 10 BCL — no NuGet reference needed. `IOptions<T>` with `ValidateOnStart()` ships in `Microsoft.Extensions.Hosting` (already referenced). Cursor-based pagination uses Lucene's `SearchAfter` API already in the dependency graph. The only meaningful change to `Directory.Packages.props` is bumping three Roslyn packages from 4.12.0 to 4.14.0 and removing the `VersionOverride` workaround.

**Core technologies:**
- `Microsoft.CodeAnalysis.CSharp` 4.14.0 — Roslyn symbol graph builder — aligns with BenchmarkDotNet's transitive requirement, eliminates VersionOverride conflict in one move
- `System.Threading.RateLimiting` (BCL, .NET 10) — `TokenBucketRateLimiter` for per-session tool call rate limiting — no new package; correct abstraction for stdio transport (not ASP.NET middleware)
- `Microsoft.Extensions.Options` `ValidateOnStart()` (already referenced) — startup config validation — zero-dependency addition
- Lucene.Net `SearchAfter` (already referenced) — idiomatic pagination over BM25 index — no new library

**What NOT to add:** `Microsoft.CodeAnalysis` 5.0.0 (MSBuildWorkspace compat unverified, no v1.5 feature requires it), `Microsoft.AspNetCore.RateLimiting` (requires ASP.NET pipeline — wrong abstraction for stdio), any external pagination library.

### Expected Features

**Must have (table stakes):**
- Roslyn 4.14 upgrade + full package audit — foundational; do first; current 4.12.0 conflicts with BDN transitive dep and will resurface as NU1107 on next SDK update
- Startup config validation — `ValidateOnStart()` + filesystem accessibility check; operators expect container-fail-fast at startup, not opaque runtime errors
- O(1) symbol lookup — fix known O(E) linear edge scan in `KnowledgeQueryService.GetSymbolAsync`; prerequisite for `find_implementations` at scale
- Pagination on `get_references` — consistency with `search_symbols`; offset/limit pattern already established in codebase
- `find_implementations` tool — first-class code navigation primitive; agents currently must parse `get_references` and filter manually — unreliable
- `doc_coverage` tool — exposes existing Roslyn analyzer metrics as a queryable MCP tool; pure snapshot post-processing, no new ingestion required
- Rate limiting — `TokenBucketRateLimiter` on tool dispatch; prevents stuck-agent retry storms from exhausting server resources
- CLAUDE.md refresh — stale docs cause agents to call nonexistent or wrong-signature tools; do last, after all tools land

**Should have (competitive):**
- Search metadata caching — TTL cache on manifest reads in `KnowledgeQueryService.ResolveSnapshotAsync`; reduces unnecessary disk I/O under concurrent queries
- Batched project resolution — fix N+1 sequential `GetSymbolAsync` calls in `DocTools.GetReferences` via new `GetSymbolsBatchAsync` interface method

**Defer (v2+):**
- Embeddings/vector search — `IVectorIndex` interface already reserved; defer pending embedding provider decision
- HTTP/SSE transport — out of scope until remote multi-client use case is validated
- Real-time file watcher auto-ingestion — breaks reproducibility guarantees; offer `--watch` CLI flag in v2
- Query DSL over symbol graph — speculative long-term

### Architecture Approach

The system has a clean six-layer pipeline (Core → Ingestion → Indexing → McpServer → AppHost → Tests) with strict boundary enforcement. v1.5 changes are contained: Core contract additions (three new interface methods, one new response type, one field addition), Indexing implementation changes (`BM25SearchIndex` + `KnowledgeQueryService`), and McpServer infrastructure additions (`RateLimitFilter`, `DocAgentServerOptionsValidator`, `StartupValidationService`) alongside tool surface extensions. No new projects are required.

**Major components and v1.5 responsibilities:**
1. `DocAgent.Core/Abstractions.cs` — add `FindImplementationsAsync`, `GetDocCoverageAsync`, `GetSymbolsBatchAsync` to `IKnowledgeQueryService`; all downstream layers compile against this first
2. `DocAgent.Core/QueryTypes.cs` — add `DocCoverageReport` record; add `TotalCount` to `ResponseEnvelope<T>` (default 0 for backward compat)
3. `DocAgent.Indexing/BM25SearchIndex` — add `_edgesByFrom` + `_edgesByTo` dictionaries built in `PopulateNodes`; follow existing `_nodes` dict pattern
4. `DocAgent.Indexing/KnowledgeQueryService` — implement new interface methods; add TTL manifest cache (SemaphoreSlim-guarded); use edge index for O(1) lookups
5. `DocAgent.McpServer/Filters/RateLimitFilter` — new MCP SDK filter, parallel to existing `AuditFilter`; single cross-cutting enforcement point for all 12+ tools
6. `DocAgent.McpServer/Config` — `DocAgentServerOptionsValidator` (`IValidateOptions<T>`) + `StartupValidationService` (`IHostedService` with stderr logging for Aspire compatibility)
7. `DocAgent.McpServer/Tools/DocTools` — add `find_implementations`, `get_doc_coverage` tools; wire batch lookup; expose `totalCount` in search responses

**Key patterns to follow:**
- MCP SDK filter decorator for cross-cutting policy (rate limiting follows `AuditFilter` pattern exactly — do not inject limiter into individual tool classes)
- Snapshot-keyed dictionaries built at `IndexAsync` time (edge dicts follow `_nodes` dict pattern — supplementary lookup only, list remains canonical)
- `IOptions<T>` + `IValidateOptions<T>` + `ValidateOnStart()` for configuration invariants

### Critical Pitfalls

1. **Roslyn package family partial upgrade** — All `Microsoft.CodeAnalysis.*` packages (CSharp, CSharp.Workspaces, Workspaces.MSBuild, Common) must move to 4.14.0 together in one PR. Verify with `dotnet restore --verbosity detailed` — zero NU1107 across ALL projects including Benchmarks and Analyzers. Also verify `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2` is compatible before proceeding.

2. **O(1) Dictionary introduction breaks determinism** — `Dictionary` iteration order is not guaranteed in .NET. The existing `List<SymbolNode>` provides stable insertion-order iteration used by serialisation and `Verify.Xunit` golden files. Add `_edgesByFrom`/`_edgesByTo` as supplementary lookup structures; keep the list as the canonical ordered collection for all output paths. Use `SortedDictionary` if iteration order is needed from the dict itself.

3. **Pagination backward-compatibility** — Adding `offset`/`limit` to existing tools is nominally backward-compatible, but changing the default return count is a semantic breaking change. Default `limit` must be at least as large as the current unbounded maximum, or existing callers silently get truncated results. Return pagination metadata only when `limit` is explicitly supplied — calls without `limit` must return the same response shape as before.

4. **`find_implementations` returning stub nodes** — `NodeKind.Stub` nodes (synthesized for NuGet/BCL types) participate in the edge graph but have null `SourceSpan.FilePath`. Without explicit filtering, agents receive navigation results that point to non-existent files. Add stub filtering to the traversal helper from the start; add a unit test with an explicit stub node in the implementation chain asserting exclusion.

5. **Startup validation silent failure under Aspire** — `ValidateOnStart()` throws during `IHost.StartAsync()`; Aspire captures exit code but not stderr of the child process. Wrap in try/catch in `Program.cs` that writes `OptionsValidationException.Message` to `Console.Error` before rethrowing. Test the validator in isolation with `ServiceCollection` directly — do not rely on Aspire integration tests for validation logic coverage.

6. **Rate limiter shared mutable state** — Rate limiter must be a DI singleton (not a static field, not a per-call using block). Honor incoming `CancellationToken` in `WaitAsync`. Return structured error response on rejection — unhandled exceptions become opaque MCP errors. Ingestion tool calls should not count against the query rate limit; rate limiting scopes to tool dispatch only.

---

## Implications for Roadmap

Based on research, the dependency graph dictates a six-phase ordering. Phases 2 and 3 can be parallelised across worktrees.

### Phase 1: Dependency Foundation
**Rationale:** The Roslyn version conflict is a latent build hazard. BenchmarkDotNet already pulls `Microsoft.CodeAnalysis.Common` 4.14.0 transitively, creating the existing VersionOverride workaround in `DocAgent.Tests.csproj`. Building any feature on 4.12.0 while this conflict exists risks mixed-assembly issues. Resolving this first gives a clean foundation — removing the VersionOverride hack is a correctness fix, not optional cleanup.
**Delivers:** Clean `dotnet restore` with zero NU1107 across all projects; VersionOverride removed from `DocAgent.Tests.csproj`; `NuGetAudit` MSBuild property enabled in `Directory.Build.props`; full package audit completed
**Addresses:** Roslyn 4.14 upgrade + package audit (FEATURES.md P1)
**Avoids:** Pitfall 1 (Roslyn partial upgrade — all five CodeAnalysis packages move together)

### Phase 2: Core Contracts + Indexing Performance
**Rationale:** Core interface additions (`IKnowledgeQueryService` methods) must precede all downstream implementation — all test doubles and tool classes compile against these contracts. O(1) edge indexing must precede `find_implementations` (Phase 5) which depends on fast edge traversal at scale. These are tightly coupled and should ship in the same phase.
**Delivers:** Updated `Abstractions.cs` (three new methods) and `QueryTypes.cs` (new record, new field); `_edgesByFrom`/`_edgesByTo` in `BM25SearchIndex.PopulateNodes`; O(1) edge lookups in `KnowledgeQueryService.GetSymbolAsync` and `GetReferencesAsync`; TTL manifest cache; `GetSymbolsBatchAsync` batch lookup; `InMemorySearchIndex` test double updated
**Uses:** Existing `_nodes` dict pattern; `SemaphoreSlim` for cache thread safety
**Avoids:** Pitfall 2 (Dictionary ordering — list retained as canonical ordered collection)

### Phase 3: McpServer Infrastructure (parallelisable with Phase 2)
**Rationale:** Rate limiting and startup validation are cross-cutting server policies with no dependency on indexing changes. Both can be built in a separate worktree in parallel with Phase 2. Infrastructure registration must precede tool additions (Phase 5).
**Delivers:** `RateLimitFilter` registered alongside `AuditFilter`; `DocAgentServerOptionsValidator` + `StartupValidationService`; `RateLimitOptions` nested config class; `ValidateOnStart()` with explicit stderr logging; unit tests for validator in isolation
**Uses:** `System.Threading.RateLimiting` BCL (no new package); `IOptions<T>` + `IValidateOptions<T>` pattern
**Avoids:** Pitfall 5 (Aspire silent failure — explicit stderr before rethrow), Pitfall 6 (rate limiter shared state — DI singleton)

### Phase 4: Pagination Extension
**Rationale:** Pagination changes touch `QueryTypes.cs`, `KnowledgeQueryService`, and `DocTools` — the same files as tool additions in Phase 5. Isolating into its own phase makes backward-compat regression testing explicit before new tools land on the same files. The design decision (default behavior unchanged) must be enforced before writing code.
**Delivers:** `get_references` with `offset`/`limit` parameters consistent with `search_symbols`; `TotalCount` in `ResponseEnvelope<T>` (default 0, non-breaking); all existing integration tests pass with no `limit` argument; `Verify.Xunit` snapshots updated once for the new `totalCount` field
**Avoids:** Pitfall 3 (pagination breaking existing clients — backward-compat contract enforced in design)

### Phase 5: New MCP Tools
**Rationale:** `find_implementations` and `get_doc_coverage` depend on the O(1) edge index (Phase 2), the updated Core contracts (Phase 2), and the infrastructure registered in Phase 3. This is the final code phase and depends on all prior phases completing.
**Delivers:** `find_implementations` MCP tool (stub-filtered O(1) edge traversal with offset/limit); `get_doc_coverage` MCP tool (snapshot post-processing, grouped by project/namespace/type); batch lookup wired in `DocTools.GetReferences`; all tools covered by PathAllowlist and PromptInjectionScanner; unit test asserting stub node exclusion
**Avoids:** Pitfall 4 (stub nodes in `find_implementations` — filtered from traversal helper in initial design)

### Phase 6: Documentation Refresh
**Rationale:** CLAUDE.md is consumed by agents as the primary API reference. Updating it before all 14 tools have stable signatures risks documenting provisional behavior. Documentation refresh is the correct last step — it describes what shipped, not what is planned.
**Delivers:** CLAUDE.md updated from 12-tool to 14-tool surface with correct parameter signatures for all tools; verified against actual `[McpServerTool]` registered methods in codebase

### Phase Ordering Rationale

- Roslyn upgrade must be first — all subsequent code compiles against it; a NU1107 mid-feature is expensive to diagnose
- Core contracts must precede Indexing implementation — but Phases 2 and 3 can be parallelised across worktrees since they touch different files
- Pagination (Phase 4) isolated from new tools (Phase 5) to contain backward-compat regression risk
- Documentation always last — reflects what shipped

### Research Flags

Phases with well-documented patterns (standard — skip `/gsd:research-phase`):
- **Phase 1 (Dependency Foundation):** Mechanical package version bump; process fully documented in STACK.md and PITFALLS.md
- **Phase 3 (Infrastructure):** `IOptions<T>` + MCP SDK filter patterns are established; code locations identified precisely in ARCHITECTURE.md
- **Phase 4 (Pagination):** Pattern already established in `search_symbols`; extend to `get_references` following identical approach
- **Phase 6 (Documentation):** Non-code; no research needed

Phases that may benefit from targeted design decisions during planning:
- **Phase 2 (Core + Indexing):** The `ISnapshotChanged` event pattern for cache invalidation (IngestionService → KnowledgeQueryService) has a potential DI cycle risk. Design the invalidation mechanism before implementation — options are an `IObserver`/event pattern or a dedicated `ISnapshotChangeNotifier` abstraction.
- **Phase 5 (New Tools):** Confirm that `SymbolEdgeKind.Implements` and `SymbolEdgeKind.Overrides` edges are populated at ingestion time for the common use cases (interface implementations, abstract overrides) before committing to the edge-graph traversal approach over live `SymbolFinder`. If edge population is incomplete, the live `Solution` approach may be needed.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All package versions verified against NuGet; BCL API verified against official .NET docs; no new packages required |
| Features | HIGH | MCP spec confirmed for pagination scope; Roslyn API confirmed for `SymbolFinder`; existing codebase read directly for all integration points |
| Architecture | HIGH | Derived entirely from direct source reading of shipped code; integration points are file-and-line specific with code excerpts |
| Pitfalls | HIGH | Version conflict verified against existing `VersionOverride` in codebase; determinism requirement explicit in PROJECT.md and CLAUDE.md; rate limiter and Aspire patterns are .NET-documented behavior |

**Overall confidence:** HIGH

### Gaps to Address

- **Cache invalidation cycle risk:** `IngestionService` → `KnowledgeQueryService` cache invalidation signal after snapshot save may create a DI circular dependency. Resolve during Phase 2 planning — options are an `IObserver`/event pattern or a dedicated `ISnapshotChangeNotifier` abstraction injected into both services.
- **Analyzer testing package Roslyn floor:** `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2` and `Microsoft.CodeAnalysis.Testing.Verifiers.XUnit 1.1.2` have their own Roslyn transitive dependency. Compatibility with 4.14.0 must be verified at the start of Phase 1 — if they cap at 4.12.0, the VersionOverride pattern must remain on those test packages only.
- **Edge population completeness for `find_implementations`:** ARCHITECTURE.md recommends edge-graph traversal (avoiding live `MSBuildWorkspace` warm-up cost), but this only works if `SymbolEdgeKind.Implements`/`Overrides` edges are comprehensively populated during ingestion. Validate during Phase 5 planning before committing to the approach.

---

## Sources

### Primary (HIGH confidence)
- `src/DocAgent.Core/Abstractions.cs` — interface contracts (direct read)
- `src/DocAgent.Core/QueryTypes.cs` — response types (direct read)
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — facade implementation with O(E) scan identified at lines ~101-117 (direct read)
- `src/DocAgent.Indexing/BM25SearchIndex.cs` — existing `_nodes` dict pattern (direct read)
- `src/DocAgent.McpServer/Tools/DocTools.cs` — all 5 existing tool implementations (direct read)
- `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` — current config shape (direct read)
- `src/Directory.Packages.props` — current package versions including VersionOverride (direct read)
- [NuGet: Microsoft.CodeAnalysis.CSharp 4.14.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.14.0) — release date May 15, 2025 confirmed
- [Microsoft Learn: Rate limiting for .NET](https://devblogs.microsoft.com/dotnet/announcing-rate-limiting-for-dotnet/) — BCL `TokenBucketRateLimiter` design rationale
- [Microsoft Learn: Auditing NuGet package dependencies](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) — `NuGetAudit` MSBuild property
- [MCP Pagination Specification (2025-03-26)](https://modelcontextprotocol.io/specification/2025-03-26/server/utilities/pagination) — list-operation cursor scope confirmed
- [SymbolFinder.FindImplementationsAsync — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder.findimplementationsasync) — API signature confirmed

### Secondary (MEDIUM confidence)
- [fast.io: MCP Server Rate Limiting](https://fast.io/resources/mcp-server-rate-limiting/) — stdio token bucket pattern guidance
- [MCP in .NET Pagination Docs](https://mcpindotnet.github.io/docs/concepts/architecture-overview/layers/data-layer/utility-features/pagination/) — community docs on pagination scope
- Aspire `ValidateOnStart()` stderr capture behavior — based on Aspire process model; defensive stderr logging is best practice regardless of direct confirmation

---
*Research completed: 2026-03-04*
*Ready for roadmap: yes*
