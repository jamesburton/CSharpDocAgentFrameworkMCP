# Architecture Research

**Domain:** MCP server for compiler-grade .NET symbol graph — v1.5 Robustness integration
**Researched:** 2026-03-04
**Confidence:** HIGH (derived from direct source reading; all findings are from shipped code, not speculation)

---

## System Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          MCP Tool Layer (McpServer)                       │
│  ┌──────────┐  ┌─────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ DocTools │  │ ChangeTools │  │ SolutionTools│  │ IngestionTools   │  │
│  └────┬─────┘  └──────┬──────┘  └──────┬───────┘  └────────┬─────────┘  │
│       │               │                │                    │            │
│  ┌────▼───────────────▼────────────────▼────────────────────▼──────────┐ │
│  │  IKnowledgeQueryService (KnowledgeQueryService in DocAgent.Indexing) │ │
│  └───────────────────────┬──────────────────────────────────────────────┘ │
│                          │ ISearchIndex + SnapshotStore                   │
│  ┌───────────────────────▼──────────────────────────────────────────────┐ │
│  │  BM25SearchIndex (Lucene.Net)  |  SnapshotStore (MessagePack files)  │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
│  ┌──────────────────────────────────────────────────────────────────────┐ │
│  │  Security: PathAllowlist, AuditLogger, PromptInjectionScanner        │ │
│  └──────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────┘
           ^ Ingestion pipeline (separate path)
┌──────────┴──────────────────────────────────────────────────────────────┐
│  IProjectSource -> IDocSource -> ISymbolGraphBuilder -> ISearchIndex.Index │
│  (DocAgent.Ingestion)          IncrementalIngestionEngine                │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Component Responsibilities

| Component | Project | Responsibility |
|-----------|---------|----------------|
| `DocTools` | McpServer | 5 read-path MCP tools: search_symbols, get_symbol, get_references, diff_snapshots, explain_project |
| `ChangeTools` | McpServer | 3 review MCP tools: review_changes, find_breaking_changes, explain_change |
| `SolutionTools` | McpServer | 2 solution tools: explain_solution, diff_solution_snapshots |
| `IngestionTools` | McpServer | 2 write-path tools: ingest_project, ingest_solution |
| `KnowledgeQueryService` | Indexing | Facade: wires ISearchIndex + SnapshotStore; handles snapshot resolution, pagination, filtering, staleness detection |
| `BM25SearchIndex` | Indexing | Lucene.Net BM25 search; in-memory `_nodes` dict for O(1) GetAsync; CamelCase tokenizer |
| `SnapshotStore` | Ingestion | MessagePack file store; ListAsync (manifest), LoadAsync (by hash) |
| `PathAllowlist` | McpServer/Security | Default-deny filesystem access control |
| `AuditLogger` | McpServer/Security | Per-call audit trail |
| `AuditFilter` | McpServer/Filters | MCP SDK filter that invokes AuditLogger |
| `DocAgentServerOptions` | McpServer/Config | All server config including AllowedPaths, ArtifactsDir, IngestionTimeoutSeconds |
| `ServiceCollectionExtensions` | McpServer | DI wiring; closure-based singleton dir resolution |

---

## Project Structure (existing)

```
src/
+-- DocAgent.Core/              # Domain types, interfaces — no IO
|   +-- Abstractions.cs         # IProjectSource, IDocSource, ISymbolGraphBuilder, ISearchIndex, IKnowledgeQueryService
|   +-- Symbols.cs              # SymbolNode, SymbolEdge, SymbolGraphSnapshot, SymbolId, enums
|   +-- QueryTypes.cs           # QueryResult<T>, ResponseEnvelope<T>, SearchResultItem, SymbolDetail, GraphDiff
|   +-- SolutionTypes.cs        # SolutionSnapshot, ProjectEntry, ProjectEdge
|   +-- DiffTypes.cs            # Semantic diff result types
|
+-- DocAgent.Ingestion/         # Source discovery, parsing, graph building
|   +-- SnapshotStore.cs        # MessagePack file store (ListAsync, LoadAsync, SaveAsync)
|   +-- IncrementalIngestionEngine.cs   # SHA-256 file manifest, skip-unchanged
|   +-- RoslynSymbolGraphBuilder.cs     # Roslyn MSBuildWorkspace graph builder
|   +-- FileHashManifest.cs     # Per-file SHA-256 tracking
|
+-- DocAgent.Indexing/          # BM25 index + query facade
|   +-- BM25SearchIndex.cs      # Lucene.Net, _nodes dict (O(1) GetAsync), CamelCase tokenizer
|   +-- KnowledgeQueryService.cs # IKnowledgeQueryService impl; pagination via Skip/Take
|   +-- InMemorySearchIndex.cs  # Test double
|   +-- CamelCaseAnalyzer.cs    # Tokenizer for symbol names
|
+-- DocAgent.McpServer/         # MCP tools, security, DI wiring
|   +-- Tools/DocTools.cs       # 5 tools; calls IKnowledgeQueryService
|   +-- Tools/ChangeTools.cs    # 3 tools; calls ChangeReviewer + SnapshotStore
|   +-- Tools/SolutionTools.cs  # 2 tools
|   +-- Tools/IngestionTools.cs # 2 tools; calls IIngestionService, ISolutionIngestionService
|   +-- Security/               # PathAllowlist, AuditLogger, PromptInjectionScanner
|   +-- Filters/AuditFilter.cs  # MCP SDK filter; parallel mount point for rate limiting
|   +-- Config/DocAgentServerOptions.cs
|   +-- ServiceCollectionExtensions.cs
|   +-- Ingestion/              # IngestionService, SolutionIngestionService, IncrementalSolutionIngestionService
|
+-- DocAgent.AppHost/           # Aspire host, telemetry
+-- DocAgent.Analyzers/         # netstandard2.0; DocCoverage, DocParity, SuspiciousEdit
+-- DocAgent.Benchmarks/        # BenchmarkDotNet; separate project for Roslyn dep isolation
```

---

## v1.5 Feature Integration Points

### 1. O(1) Symbol Lookup

**Current state:** `BM25SearchIndex.GetAsync` is already O(1) via the `_nodes` dictionary. The actual bottleneck is `KnowledgeQueryService.GetSymbolAsync` which iterates `snapshot.Edges` linearly (O(E)) to build parent/child/related navigation hints for `SymbolDetail`.

**Root cause code (KnowledgeQueryService.cs lines ~101-117):**
```csharp
foreach (var edge in snapshot.Edges)   // O(E) scan per call
{
    if (edge.Kind == SymbolEdgeKind.Contains)
    {
        if (edge.To == id) parentId = edge.From;
        else if (edge.From == id) childIds.Add(edge.To);
    }
    ...
}
```

**Integration point:** `DocAgent.Indexing` — modify `BM25SearchIndex` and `KnowledgeQueryService`.

**Recommended approach:**
- Add two edge-lookup dictionaries to `BM25SearchIndex` populated during `PopulateNodes`:
  - `_edgesByFrom: Dictionary<SymbolId, List<SymbolEdge>>`
  - `_edgesByTo: Dictionary<SymbolId, List<SymbolEdge>>`
- Expose via an internal method or new `IEdgeLookup` interface so `KnowledgeQueryService` can use them.
- `KnowledgeQueryService.GetSymbolAsync` replaces the foreach scan with O(1) lookups.
- Also fixes `GetReferencesAsync` (currently also scans `snapshot.Edges`).
- Classification: **MODIFY** `BM25SearchIndex.cs`, **MODIFY** `KnowledgeQueryService.cs`. No new projects.

---

### 2. Pagination for Large Result Sets

**Current state:** Pagination parameters (`offset`, `limit`) exist at all three layers — `IKnowledgeQueryService.SearchAsync` interface, `KnowledgeQueryService.SearchAsync` implementation (uses `Skip/Take`), and `DocTools.SearchSymbols` tool. **Structural pagination is already implemented.**

**Gap:** `KnowledgeQueryService.SearchAsync` collects ALL filtered items into a `List<SearchResultItem>` before slicing, but the JSON response only exposes the count of the page (`total = sanitizedItems.Count`), not the full pre-page count. Agents cannot determine if more pages exist.

**Integration point:** `DocAgent.Core/QueryTypes.cs` (add total count field) + `DocAgent.Indexing/KnowledgeQueryService.cs` + `DocAgent.McpServer/Tools/DocTools.cs`.

**Recommended approach:**
- Add `int TotalCount` to `ResponseEnvelope<T>` in `QueryTypes.cs` (default 0 for non-search responses, avoiding breaking change). Alternatively add a new `PagedResponseEnvelope<T>` that extends `ResponseEnvelope<T>`.
- `KnowledgeQueryService.SearchAsync` already has `filtered.Count` available before `Skip/Take` — pass it through.
- `DocTools.SearchSymbols` surfaces `totalCount` in JSON response.
- Classification: **MODIFY** `QueryTypes.cs` (Core), **MODIFY** `KnowledgeQueryService.cs` (Indexing), **MODIFY** `DocTools.cs` (McpServer).

---

### 3. `find_implementations` Tool

**Current state:** `SymbolEdgeKind.Implements` and `SymbolEdgeKind.Overrides` edge kinds exist. `GetReferencesAsync` returns all edges for a symbol but has no kind filter. No dedicated "implementations" query or MCP tool exists.

**Integration point:** `DocAgent.Core/Abstractions.cs` (extend interface) + `DocAgent.Indexing/KnowledgeQueryService.cs` + `DocAgent.McpServer/Tools/DocTools.cs`.

**Recommended approach:**
- Add to `IKnowledgeQueryService` in `Abstractions.cs`:
  ```csharp
  IAsyncEnumerable<SymbolNode> FindImplementationsAsync(
      SymbolId interfaceOrAbstractId,
      CancellationToken ct = default);
  ```
- Implement in `KnowledgeQueryService`: using the O(1) edge index from item 1, filter edges where `Kind == Implements || Kind == Overrides` AND `To == interfaceOrAbstractId`, then resolve each `From` via `_nodes[From]`.
- Add `find_implementations` MCP tool to `DocTools.cs`. If `DocTools` already has 5+ methods consider a new `QueryTools.cs` class decorated with `[McpServerToolType]` — MCP SDK supports multiple tool type classes registered in DI.
- Apply `PromptInjectionScanner.Scan` on node docs in results.
- Classification: **MODIFY** `Abstractions.cs` (Core), **MODIFY** `KnowledgeQueryService.cs` (Indexing), **MODIFY** `DocTools.cs` or **NEW** `QueryTools.cs` (McpServer).

---

### 4. Doc Coverage Metrics Tool

**Current state:** `DocAgent.Analyzers/Coverage/DocCoverageAnalyzer.cs` enforces doc coverage at build time via Roslyn analyzer. No runtime query exists over the ingested snapshot. `SymbolNode.Docs` is nullable — `null` means undocumented. `Accessibility` and `NodeKind` fields allow filtering to public real symbols.

**Integration point:** `DocAgent.Core` (new response type + interface method) + `DocAgent.Indexing/KnowledgeQueryService.cs` + `DocAgent.McpServer/Tools/DocTools.cs`.

**Recommended approach:**
- Add to `QueryTypes.cs`:
  ```csharp
  public sealed record DocCoverageReport(
      int TotalPublicSymbols,
      int DocumentedCount,
      int UndocumentedCount,
      double CoveragePercent,
      IReadOnlyList<SymbolId> UndocumentedIds,
      string? ProjectFilter);
  ```
- Add to `IKnowledgeQueryService`:
  ```csharp
  Task<QueryResult<ResponseEnvelope<DocCoverageReport>>> GetDocCoverageAsync(
      string? projectFilter = null,
      CancellationToken ct = default);
  ```
- `KnowledgeQueryService` implementation: resolve snapshot, filter `_nodes.Values` to `Real` kind + `Public` accessibility (optionally filtered by `ProjectOrigin`), partition by `Docs?.Summary != null`.
- Compute `UndocumentedIds` as a list of `SymbolId` for actionable output.
- Add `get_doc_coverage` MCP tool to `DocTools.cs` or a new `MetricsTools.cs`.
- The Roslyn analyzer (`DocCoverageAnalyzer`) is independent and does not change — it operates at build time, this operates at runtime over the snapshot graph.
- Classification: **NEW** `DocCoverageReport` in `QueryTypes.cs`, **MODIFY** `Abstractions.cs` (Core), **MODIFY** `KnowledgeQueryService.cs` (Indexing), **MODIFY** `DocTools.cs` or **NEW** `MetricsTools.cs` (McpServer).

---

### 5. Rate Limiting

**Current state:** No rate limiting exists. The existing `AuditFilter` in `McpServer/Filters/AuditFilter.cs` is the only MCP SDK filter registered. `DocAgentServerOptions` has `IngestionTimeoutSeconds` but no rate limit configuration.

**Integration point:** `DocAgent.McpServer` only — server policy, not a query or indexing concern.

**Recommended approach:**
- Add a `RateLimitOptions` nested class to `DocAgentServerOptions`:
  ```csharp
  public RateLimitOptions RateLimit { get; set; } = new();

  public sealed class RateLimitOptions
  {
      public int MaxRequestsPerMinute { get; set; } = 0;    // 0 = disabled
      public int MaxConcurrentRequests { get; set; } = 0;  // 0 = disabled
  }
  ```
- Create `McpServer/Filters/RateLimitFilter.cs` implementing the MCP SDK filter contract (parallel pattern to `AuditFilter`).
- Register `System.Threading.RateLimiting.FixedWindowRateLimiter` and/or `ConcurrencyLimiter` in `ServiceCollectionExtensions.AddDocAgent` when options are non-zero.
- Return `{ "error": "rate_limit_exceeded", "retryAfterSeconds": N }` consistent with existing `ErrorResponse` format in `DocTools`.
- Do NOT inject `RateLimiter` into individual tool classes — centralise in the filter.
- Classification: **NEW** `RateLimitFilter.cs` in `McpServer/Filters/`, **MODIFY** `DocAgentServerOptions.cs`, **MODIFY** `ServiceCollectionExtensions.cs`.

---

### 6. Startup Validation

**Current state:** `ServiceCollectionExtensions.AddDocAgent` creates `resolvedDir` eagerly via `Directory.CreateDirectory` (will throw on permission errors), but no structured validation or startup summary exists. `DocAgentServerOptions` has no `[Required]` or validation attributes.

**Integration point:** `DocAgent.McpServer/Config/` only.

**Recommended approach:**
- Add `DocAgentServerOptionsValidator : IValidateOptions<DocAgentServerOptions>` in `McpServer/Config/`:
  - Check `IngestionTimeoutSeconds > 0`
  - Check `ArtifactsDir` is writable if non-null
  - Check rate limit values are non-negative
  - Return `ValidateOptionsResult.Fail(...)` with actionable message
- Register in `ServiceCollectionExtensions` and call `.ValidateOnStart()`:
  ```csharp
  services.AddOptions<DocAgentServerOptions>().ValidateOnStart();
  services.AddSingleton<IValidateOptions<DocAgentServerOptions>, DocAgentServerOptionsValidator>();
  ```
- Add `StartupValidationService : IHostedService` that logs effective configuration at `Information` level on startup (useful for diagnostics). Runs after options validation passes.
- Classification: **NEW** `DocAgentServerOptionsValidator.cs`, **NEW** `StartupValidationService.cs`, **MODIFY** `ServiceCollectionExtensions.cs`.

---

### 7. Search Metadata Caching

**Current state:** `KnowledgeQueryService.ResolveSnapshotAsync` calls `_snapshotStore.ListAsync(ct)` on every query — a disk read of the snapshot manifest JSON. For high-frequency queries (e.g. rapid `search_symbols` calls), this is unnecessary I/O.

**Integration point:** `DocAgent.Indexing/KnowledgeQueryService.cs`.

**Recommended approach:**
- Add a private `SemaphoreSlim`-guarded manifest cache in `KnowledgeQueryService`:
  ```csharp
  private IReadOnlyList<SnapshotManifestEntry>? _manifestCache;
  private DateTimeOffset _manifestCacheExpiry;
  private readonly SemaphoreSlim _cacheLock = new(1, 1);
  private const int ManifestCacheTtlSeconds = 5;
  ```
- `ResolveSnapshotAsync` uses cache if within TTL; otherwise re-reads from disk.
- Add `InvalidateManifestCache()` internal method; call it from `IngestionService` after `SaveAsync` (requires IngestionService to hold a reference to `KnowledgeQueryService` — check if this creates a cycle via DI, and if so, use an `IObserver` pattern or `ISnapshotChanged` event instead).
- Classification: **MODIFY** `KnowledgeQueryService.cs` (Indexing). Optional: **MODIFY** `IngestionService.cs` or **NEW** event/notification type.

---

### 8. Batched Project Resolution (fix for GetReferences N+1)

**Current state:** `DocTools.GetReferences` resolves `ProjectOrigin` for every edge endpoint via sequential `_query.GetSymbolAsync` calls:
```csharp
foreach (var nodeId in uniqueIds)
{
    var nodeResult = await _query.GetSymbolAsync(nodeId, ct: cancellationToken);  // N calls
    if (nodeResult.Success)
        nodeProjectCache[nodeId] = nodeResult.Value!.Payload.Node.ProjectOrigin;
}
```
For a symbol with many cross-project edges this is N sequential calls when one batch lookup of `_nodes` would suffice.

**Integration point:** `DocAgent.Core/Abstractions.cs` + `DocAgent.Indexing/KnowledgeQueryService.cs` + `DocAgent.McpServer/Tools/DocTools.cs`.

**Recommended approach:**
- Add to `IKnowledgeQueryService`:
  ```csharp
  Task<IReadOnlyDictionary<SymbolId, SymbolNode>> GetSymbolsBatchAsync(
      IReadOnlyList<SymbolId> ids,
      CancellationToken ct = default);
  ```
- Implement in `KnowledgeQueryService`: resolve snapshot once, return `_nodes[id]` for each requested id — O(K) where K is the batch size.
- `DocTools.GetReferences` replaces the sequential loop with a single `GetSymbolsBatchAsync` call.
- Classification: **MODIFY** `Abstractions.cs` (Core), **MODIFY** `KnowledgeQueryService.cs` (Indexing), **MODIFY** `DocTools.cs` (McpServer).

---

### 9. Roslyn 4.14 Upgrade + Package Audit

**Current state:** `Microsoft.CodeAnalysis.CSharp` pinned at `4.12.0` in `src/Directory.Packages.props`. `DocAgent.Benchmarks` has relaxed Roslyn version constraints to avoid conflicts with BenchmarkDotNet transitive dependencies.

**Integration point:** `src/Directory.Packages.props`. No architectural changes expected. Roslyn 4.x has stable public API; verify Roslyn.Workspaces changes if any.

**Risk:** Benchmark project isolation pattern must be re-verified after upgrade. If BenchmarkDotNet still pulls a conflicting Roslyn, the `DocAgent.Benchmarks` project may need a separate `<PackageReference>` override.

---

### 10. CLAUDE.md Refresh

**Current state:** CLAUDE.md documents the 12-tool surface. After v1.5 adds `find_implementations` and `get_doc_coverage`, it will be 14 tools.

**Integration point:** Documentation only.

---

## New vs Modified Components Summary

| Component | Status | Layer | Notes |
|-----------|--------|-------|-------|
| Edge index dicts in `BM25SearchIndex` | NEW fields | Indexing | `_edgesByFrom` + `_edgesByTo`; built during `PopulateNodes` |
| `FindImplementationsAsync` on `IKnowledgeQueryService` | MODIFY interface | Core | New method; test doubles must be updated |
| `GetDocCoverageAsync` on `IKnowledgeQueryService` | MODIFY interface | Core | New method; test doubles must be updated |
| `GetSymbolsBatchAsync` on `IKnowledgeQueryService` | MODIFY interface | Core | New method; eliminates N+1 in GetReferences |
| `DocCoverageReport` record | NEW type | Core/QueryTypes.cs | New response record |
| `TotalCount` on `ResponseEnvelope<T>` | MODIFY type | Core/QueryTypes.cs | Pagination metadata (default 0 = backward compat) |
| `KnowledgeQueryService` | MODIFY | Indexing | Edge index, batch lookup, cache, impl/coverage queries |
| `BM25SearchIndex` | MODIFY | Indexing | Add `_edgesByFrom`, `_edgesByTo` to `PopulateNodes` |
| `DocAgentServerOptionsValidator` | NEW | McpServer/Config | `IValidateOptions<DocAgentServerOptions>` |
| `StartupValidationService` | NEW | McpServer/Config | `IHostedService`; logs effective config on startup |
| `RateLimitFilter` | NEW | McpServer/Filters | Parallel to `AuditFilter`; single cross-cutting enforcement point |
| `DocAgentServerOptions` | MODIFY | McpServer/Config | Add `RateLimitOptions` nested class |
| `ServiceCollectionExtensions` | MODIFY | McpServer | Register validator, startup service, rate limiter |
| `DocTools` | MODIFY | McpServer/Tools | Add `find_implementations`, `get_doc_coverage`; wire batch lookup; expose `totalCount` |
| `src/Directory.Packages.props` | MODIFY | Build | Roslyn 4.14 bump + full dep audit |
| `CLAUDE.md` | MODIFY | Docs | Refresh for 14-tool surface |

---

## Recommended Build Order

Dependencies constrain this ordering:

```
Step 1: Core contracts
        Abstractions.cs — add FindImplementationsAsync, GetDocCoverageAsync, GetSymbolsBatchAsync
        QueryTypes.cs   — add DocCoverageReport, TotalCount on ResponseEnvelope
        (All downstream layers compile against these; must be first)
            |
Step 2: Indexing implementation (no McpServer dependency, parallelizable with Step 3)
        BM25SearchIndex — add _edgesByFrom/_edgesByTo to PopulateNodes
        KnowledgeQueryService — implement new interface methods, add cache, use edge index
            |
Step 3: McpServer infrastructure (parallelizable with Step 2)
        DocAgentServerOptions — add RateLimitOptions
        DocAgentServerOptionsValidator — IValidateOptions implementation
        StartupValidationService — IHostedService
        RateLimitFilter — MCP SDK filter
        ServiceCollectionExtensions — register all new services
            |
Step 4: McpServer tool surface (depends on Steps 1-3)
        DocTools — add find_implementations, get_doc_coverage, wire batch lookup, expose totalCount
            |
Step 5: Package upgrade (standalone; isolate any compile breaks from Roslyn API delta)
        Directory.Packages.props — Roslyn 4.14 + full dep audit
            |
Step 6: Documentation
        CLAUDE.md — refresh for 14-tool surface
```

---

## Data Flow: New Features

### find_implementations

```
MCP Client -> find_implementations(interfaceSymbolId)
    -> DocTools.FindImplementations
    -> IKnowledgeQueryService.FindImplementationsAsync(id)
    -> KnowledgeQueryService: resolve snapshot
       -> _edgesByTo[id] (O(1)) -> filter Kind=Implements|Overrides
       -> foreach match: _nodes[edge.From] (O(1))
    -> return IAsyncEnumerable<SymbolNode>
    -> DocTools: PromptInjectionScanner.Scan on docs -> format -> response
```

### get_doc_coverage

```
MCP Client -> get_doc_coverage(projectFilter?)
    -> DocTools.GetDocCoverage
    -> IKnowledgeQueryService.GetDocCoverageAsync(projectFilter)
    -> KnowledgeQueryService: resolve snapshot
       -> _nodes.Values (already loaded at IndexAsync time)
       -> filter: Real + Public + optional projectFilter
       -> partition by Docs?.Summary != null
       -> compute TotalPublicSymbols, DocumentedCount, CoveragePercent, UndocumentedIds
    -> return DocCoverageReport
    -> DocTools: format -> JSON/markdown/tron response
```

### Rate limiting

```
MCP Client -> any tool call
    -> RateLimitFilter.OnBeforeAsync
    -> RateLimiter.AcquireAsync (FixedWindowRateLimiter or ConcurrencyLimiter)
    -> if rejected: return { "error": "rate_limit_exceeded", "retryAfterSeconds": N }
    -> else: proceed to tool method
    -> (tool executes)
    -> RateLimitFilter.OnAfterAsync: release lease
```

### Startup validation

```
Host.StartAsync()
    -> IValidateOptions<DocAgentServerOptions>.Validate(opts)   [triggered by ValidateOnStart()]
    -> check IngestionTimeoutSeconds > 0
    -> check ArtifactsDir writable (if set)
    -> check RateLimit values >= 0
    -> if fail: throw OptionsValidationException -> host does not start
    -> if pass: StartupValidationService.StartAsync -> log effective config summary at Information
```

---

## Architectural Patterns

### Pattern 1: Decorator for cross-cutting policy (extend for rate limiting)

**What:** Wrap a concern with a policy shell via the MCP SDK filter mechanism.
**Current use:** `AuditFilter` already applies to all tool calls.
**v1.5 extension:** `RateLimitFilter` follows the same pattern.
**Trade-offs:** Clean separation; avoids duplicating enforcement in 12+ tool methods. Requires understanding MCP SDK filter contract.

### Pattern 2: IOptions + IValidateOptions for configuration validation

**What:** .NET's built-in options validation pattern. Runs at first-use or host startup.
**When to use:** Any strongly-typed configuration class with invariants.
**Example:**
```csharp
services.AddOptions<DocAgentServerOptions>().ValidateOnStart();
services.AddSingleton<IValidateOptions<DocAgentServerOptions>, DocAgentServerOptionsValidator>();
```
**Trade-offs:** Eager startup failure is user-friendly. `ValidateOnStart()` prevents misconfigured server from accepting requests.

### Pattern 3: In-memory cache with snapshot-keyed dictionaries (extend for edge index)

**What:** `BM25SearchIndex._nodes` holds all `Real` `SymbolNode`s keyed by `SymbolId`. Built during `PopulateNodes` at `IndexAsync` time.
**v1.5 extension:** `_edgesByFrom` and `_edgesByTo` dictionaries built in the same `PopulateNodes` pass.
**Trade-offs:** Memory grows with snapshot size (one dict entry per edge). Implicit eviction at next `IndexAsync` call. Acceptable for the single-snapshot-per-session design.

---

## Anti-Patterns

### Anti-Pattern 1: Linear scan of snapshot.Edges per GetSymbolAsync call

**What people do:** Iterate `snapshot.Edges` (O(E)) on every `GetSymbolAsync` to find parent/child navigation hints.
**Why it's wrong:** Already present in shipped code; becomes a bottleneck for large solutions with thousands of edges and frequent `get_symbol` calls.
**Do this instead:** Build `_edgesByFrom`/`_edgesByTo` dictionaries in `PopulateNodes` so `GetSymbolAsync` does O(1) lookups.

### Anti-Pattern 2: Adding rate limiting inside each tool method

**What people do:** Inject `RateLimiter` into every tool class and call `AcquireAsync` at the top of each method.
**Why it's wrong:** 12+ tools means 12+ enforcement points. Easy to miss one.
**Do this instead:** Single `RateLimitFilter` applied at MCP server level, parallel to `AuditFilter`.

### Anti-Pattern 3: Extending IKnowledgeQueryService without updating test doubles

**What people do:** Add methods to `IKnowledgeQueryService` in `Abstractions.cs` but forget `InMemorySearchIndex` and any test helper stubs in `DocAgent.Tests`.
**Why it's wrong:** `InMemorySearchIndex` implements `ISearchIndex`, not `IKnowledgeQueryService` directly — but `KnowledgeQueryService` unit tests likely use mock/stub implementations of the interface. New interface methods cause compile failures in those stubs.
**Do this instead:** Update all interface implementations and test doubles in the same PR as the interface change.

### Anti-Pattern 4: Loading snapshot from disk for doc coverage

**What people do:** Call `_snapshotStore.LoadAsync` to deserialize the full snapshot just to count documentation status.
**Why it's wrong:** Snapshot is already in memory as `_nodes` in `BM25SearchIndex` after indexing.
**Do this instead:** Compute doc coverage from `_nodes` directly (already populated). Query `KnowledgeQueryService` which can access `_nodes` via `ISearchIndex.GetAsync` — or expose the node collection as an indexed batch operation via `GetSymbolsBatchAsync`.

### Anti-Pattern 5: Using ConfigureAwait(false) inconsistently in async streams

**What people do:** Mix `ConfigureAwait(false)` and no-configure in `IAsyncEnumerable` methods (`GetReferencesAsync`, `FindImplementationsAsync`).
**Why it's wrong:** The existing `GetReferencesAsync` implementation uses `ConfigureAwait(false)` for the initial `ResolveSnapshotAsync` but not for the yield loop. This is intentional in async streams (the `[EnumeratorCancellation]` attribute handles the correct pattern).
**Do this instead:** Follow the existing `GetReferencesAsync` pattern exactly for `FindImplementationsAsync`.

---

## Integration Points Summary

| Boundary | Communication | v1.5 Change |
|----------|---------------|-------------|
| Core.IKnowledgeQueryService | Interface contract between Indexing and McpServer | Add 3 new methods: FindImplementationsAsync, GetDocCoverageAsync, GetSymbolsBatchAsync |
| Core.ResponseEnvelope<T> | Response wrapper type | Add TotalCount field (default 0 for backward compat) |
| Indexing.BM25SearchIndex | Stores _nodes; now also stores edge dicts | Add _edgesByFrom, _edgesByTo |
| Indexing.KnowledgeQueryService | Manifest read per query | Add TTL cache; invalidation hook from IngestionService |
| McpServer.DocTools | Calls IKnowledgeQueryService | Add find_implementations, get_doc_coverage tool methods; use batch lookup |
| McpServer.Filters | AuditFilter already registered | Add RateLimitFilter alongside it |
| McpServer.Config | DocAgentServerOptions | Add RateLimitOptions; add IValidateOptions registration |
| IngestionService -> KnowledgeQueryService | Currently none | Add cache invalidation signal after successful ingestion |

---

## Sources

- `src/DocAgent.Core/Abstractions.cs` — interface contracts (direct read)
- `src/DocAgent.Core/QueryTypes.cs` — response types (direct read)
- `src/DocAgent.Core/Symbols.cs` — domain types including SymbolEdgeKind, NodeKind (direct read)
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — full facade implementation (direct read)
- `src/DocAgent.Indexing/BM25SearchIndex.cs` — index implementation, _nodes dict pattern (direct read)
- `src/DocAgent.McpServer/Tools/DocTools.cs` — all 5 tool implementations (direct read)
- `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` — current config shape (direct read)
- `src/DocAgent.McpServer/ServiceCollectionExtensions.cs` — DI wiring pattern (direct read)
- `docs/Architecture.md` — project dependency graph (direct read)
- `.planning/PROJECT.md` — v1.5 milestone requirements and key decisions (direct read)

---

*Architecture research for: DocAgentFramework v1.5 Robustness*
*Researched: 2026-03-04*
