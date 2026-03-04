# Feature Research

**Domain:** .NET MCP server with Roslyn-based symbol query pipeline
**Researched:** 2026-03-04
**Confidence:** HIGH (MCP spec, Roslyn API docs verified) / MEDIUM (rate limiting, startup validation patterns)

## Scope Note

This is a subsequent milestone research document. The 12 existing MCP tools are already shipped. Research focuses exclusively on the **v1.5 target features**: pagination for large result sets, `find_implementations` tool, doc coverage metrics tool, rate limiting, startup config validation, CLAUDE.md refresh, Roslyn 4.14 upgrade, and performance optimisations (O(1) symbol lookup, batched project resolution, search metadata caching).

---

## Feature Landscape

### Table Stakes (Users Expect These)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| **Pagination on `get_references`** | `get_references` returns unbounded lists for widely-used types; offset/limit already exists on `search_symbols`; agents expect consistent pagination contract across all list-returning tools | MEDIUM | MCP spec cursor pagination applies to list operations (`tools/list`, etc.), **not** tool call responses. Correct approach: add `offset`/`limit` params matching `search_symbols` convention. Return `total`, `offset`, `limit`, `hasMore` in envelope. |
| **`find_implementations` tool** | "Go to Implementation" is a first-class code navigation primitive. Agents navigating interface/abstract-member hierarchies need it. Without it, users must parse `get_references` and filter manually — unreliable. | MEDIUM | `SymbolFinder.FindImplementationsAsync(ISymbol, Solution, ...)` + `FindDerivedClassesAsync` in `Microsoft.CodeAnalysis.FindSymbols`. Requires live `Solution` — same MSBuildWorkspace pattern as ingestion. PathAllowlist enforcement required. Add offset/limit for large impl sets. |
| **Startup config validation** | Production deployments fail silently when `AllowedPaths` is empty or `ArtifactsDir` is inaccessible. Operators expect container-fail-fast behaviour at startup, not opaque runtime errors. | LOW | `ValidateOnStart()` + `ValidateDataAnnotations()` on `DocAgentServerOptions`. Add `[Required]` / `[Range]` attributes. Add `IHostedService` or `IStartupFilter` to verify `ArtifactsDir` is accessible. All changes stay inside `Config/` layer. |
| **Roslyn 4.14 upgrade + package audit** | Current pin is 4.12.0. Roslyn 4.14 ships with .NET 9 SDK. Staying on old Roslyn risks incompatibility with .NET 10 toolchain and misses upstream bug fixes. Consumers expect dependencies to track the platform. | LOW-MEDIUM | `Microsoft.CodeAnalysis.CSharp` 4.14.0 confirmed on NuGet. BenchmarkDotNet project already isolated to prevent transitive conflicts. Central package management via `Directory.Packages.props` — single-line bump. Full dep audit: MessagePack 3.x, Lucene.Net, ModelContextProtocol SDK, Aspire packages. |
| **CLAUDE.md refresh** | CLAUDE.md documents the MCP tool surface. Currently lists v1.0 tools only. Agents consuming this server via Claude Code read CLAUDE.md as primary API reference. Stale docs cause agents to call nonexistent or wrong-signature tools. | LOW | Refresh to list all 12 existing tools with correct parameter signatures. Add v1.5 tools (`find_implementations`, `doc_coverage`) after implementation. Non-code change, but critical for downstream agent usability — do last. |

### Differentiators (Competitive Advantage)

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| **`doc_coverage` tool** | Surfaces what existing Roslyn analyzers already compute (doc coverage policy) as a queryable MCP tool. Agents doing code review or PR analysis can query coverage per-namespace or per-project. Turns an internal analyzer into an agent-visible metric. | MEDIUM | Walk `SymbolGraphSnapshot.Nodes` filtering to `NodeKind.Real` (exclude stubs). Check `node.DocComment` non-null and non-empty. Group by project → namespace → type. Output: total symbols, documented symbols, coverage %, breakdown table. No new ingestion needed — pure post-processing of existing snapshot. |
| **O(1) symbol lookup by SymbolId** | Current `GetSymbolAsync` may do linear scan over snapshot. At solution scale (10K+ symbols), this is measurable latency. O(1) lookup via `Dictionary<SymbolId, SymbolNode>` built at snapshot load time gives sub-millisecond `get_symbol` and `get_references`. | LOW-MEDIUM | Build lookup dictionary in `SnapshotStore.Load()`. Cache alongside snapshot in `BM25SearchIndex` or a new `SymbolLookupCache`. BenchmarkDotNet regression guard already in place — improvement immediately visible. Prerequisite for `find_implementations` at scale. |
| **Search metadata caching** | Repeated `search_symbols` queries over unchanged snapshot may rebuild BM25 scorer context. Caching last-used snapshot version avoids redundant index hydration and reduces per-query overhead under concurrent agent usage. | MEDIUM | Track `snapshotVersion` in `BM25SearchIndex`; skip re-init if version matches. Use `SemaphoreSlim` for thread safety. Not agent-visible, but improves throughput. |
| **Batched project resolution** | Current solution ingestion resolves projects sequentially within each dependency tier. Batching 4–8 independent-tier projects in parallel reduces wall-clock ingestion time. | MEDIUM | Existing `DependencyCascade` topological sort already groups projects by tier. Each tier can be parallelised with `Task.WhenAll`. MSBuildWorkspace is not thread-safe for open operations — batch only file-level parse work after workspace open. BenchmarkDotNet guards regressions. |
| **Rate limiting on tool calls** | Prevents a stuck agent retry loop from exhausting server resources or generating excessive audit log volume. Key for multi-agent or long-running deployment scenarios. | MEDIUM | .NET 7+ `System.Threading.RateLimiting` — `TokenBucketRateLimiter` for burst tolerance or `FixedWindowRateLimiter` for strict quotas. For stdio transport, rate limiting is per-process. Simplest form: shared `RateLimiter` injected into tool classes; tools call `TryAcquire()` and return structured `rate_limited` error if denied. Configure via `DocAgentServerOptions.RateLimit`. |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **HTTP/SSE MCP transport** | Enables remote agent connections and multi-client scenarios | Adds auth, TLS, connection management complexity; stdio is sufficient for local agent use; explicitly deferred in PROJECT.md | Keep stdio; document deployment pattern (agent host runs server as stdio subprocess) |
| **Cursor persistence across MCP sessions** | MCP spec mentions cursors — clients might want to resume paginated tool queries after restart | MCP spec explicitly says clients MUST NOT persist cursors across sessions; cursor position in a snapshot may not exist after re-ingestion | Document in tool descriptions that pagination state is session-scoped; return clear error on invalid offset |
| **Embeddings/vector similarity search** | Natural language queries over doc content seem compelling | Embedding model dependency (OpenAI or local), latency, non-deterministic results, breaks reproducibility guarantee; `IVectorIndex` interface already reserved | Keep `IVectorIndex` stub; defer embeddings to v2; improve BM25 CamelCase tokenisation instead |
| **Real-time file watcher auto-ingestion** | Agents want snapshot to always be current without explicit `ingest_project` call | File watcher adds background thread complexity, debounce logic, non-deterministic ingestion timing that breaks reproducibility guarantees; SHA-256 incremental ingestion already makes explicit re-ingestion fast | Expose `ingest_project`/`ingest_solution` in CLAUDE.md with "call after build" guidance; consider `--watch` CLI flag as future V2 feature |
| **Per-symbol rate limiting** | Fine-grained control over which symbols can be queried | PathAllowlist already handles namespace/path security; per-symbol rate limiting adds O(symbol-count) state with marginal security benefit over existing model | Keep rate limiting at tool-call level; use PathAllowlist for access control |

---

## Feature Dependencies

```
[O(1) symbol lookup]
    └──enables──> [find_implementations] (fast node resolution for large impl sets)
    └──enables──> [doc_coverage] (fast full-graph traversal)

[Roslyn 4.14 upgrade]
    └──enables──> [find_implementations] (build on target Roslyn version)
    └──prerequisite for──> [analyzer correctness on .NET 10]

[Startup validation]
    └──depends on──> [DocAgentServerOptions] (already exists — add attributes + ValidateOnStart)

[Pagination on get_references]
    └──depends on──> [existing get_references tool] (shipped v1.0)
    └──same pattern as──> [search_symbols offset/limit] (already implemented)

[find_implementations pagination]
    └──same pattern as──> [Pagination on get_references]

[Rate limiting]
    └──depends on──> [DocAgentServerOptions] (add RateLimitOptions nested config)
    └──independent from──> [pagination, find_implementations, doc_coverage]

[CLAUDE.md refresh]
    └──depends on──> [all v1.5 tools complete] (document last, after tools land)
    └──independent from──> [code changes]
```

### Dependency Notes

- **`find_implementations` benefits from O(1) lookup:** Building implementation lists for widely-implemented interfaces (e.g., `IDisposable`) requires resolving many SymbolIds quickly. Build lookup cache before implementing this tool.
- **`doc_coverage` is standalone:** Pure snapshot post-processing. No dependency on lookup optimisations, but benefits from them at scale.
- **Roslyn 4.14 should precede `find_implementations`:** `SymbolFinder.FindImplementationsAsync` behaviour has improved across Roslyn releases. Build on the target version.
- **Rate limiting is cross-cutting:** Can be added as a decorator on tool dispatch without touching search/indexing logic. Can be done in parallel with other features.

---

## MVP Definition for v1.5

### Build in v1.5 (Current Milestone)

- [x] **Roslyn 4.14 upgrade + full package audit** — foundational; do first to avoid building on stale deps
- [x] **Startup config validation** — `ValidateOnStart` + filesystem accessibility check; LOW complexity, HIGH operational value
- [x] **O(1) symbol lookup + search metadata caching** — performance; unlocks `find_implementations` scalability
- [x] **Pagination on `get_references`** — consistent with `search_symbols`; offset/limit pattern already established
- [x] **`find_implementations` tool** — new MCP tool; `SymbolFinder.FindImplementationsAsync` + `FindDerivedClassesAsync`
- [x] **`doc_coverage` tool** — new MCP tool; post-processes existing snapshot; no new ingestion needed
- [x] **Rate limiting** — `TokenBucketRateLimiter` on tool dispatch; configurable via options
- [x] **CLAUDE.md refresh** — non-code; update after above tools land

### Future Consideration (v2+)

- [ ] **Embeddings/vector search** — `IVectorIndex` interface reserved; defer pending embedding provider decision
- [ ] **HTTP/SSE transport** — explicitly out of scope until remote multi-client use case is validated
- [ ] **Real-time file watcher** — optional `--watch` CLI flag; deferred
- [ ] **Query DSL over symbol graph** — speculative long-term
- [ ] **Polyglot (Tree-sitter, LSP bridge)** — future tier

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Roslyn 4.14 upgrade + dep audit | MEDIUM | LOW | P1 |
| Startup config validation | HIGH | LOW | P1 |
| O(1) symbol lookup | HIGH | LOW | P1 |
| Search metadata caching | MEDIUM | MEDIUM | P1 |
| Pagination on `get_references` | HIGH | LOW | P1 |
| `find_implementations` tool | HIGH | MEDIUM | P1 |
| `doc_coverage` tool | HIGH | MEDIUM | P1 |
| Rate limiting | MEDIUM | MEDIUM | P2 |
| Batched project resolution | MEDIUM | MEDIUM | P2 |
| CLAUDE.md refresh | HIGH | LOW | P1 (last) |

---

## Implementation Notes by Feature

### Pagination on `get_references`

MCP spec cursor-based pagination (opaque `nextCursor` token in response, `cursor` in request) applies to MCP **list operations** (`tools/list`, `resources/list`, etc.) — **not** to tool call responses, which are free-form text/JSON strings. The correct pattern for tool response pagination is the offset/limit approach already used by `search_symbols`. Add `offset` (default 0) and `limit` (default 50, max 200) parameters to `get_references`. Return `total`, `offset`, `limit`, `hasMore` in the JSON envelope.

### `find_implementations`

```
SymbolFinder.FindImplementationsAsync(ISymbol, Solution, IImmutableSet<Project>?, CancellationToken)
SymbolFinder.FindDerivedClassesAsync(INamedTypeSymbol, Solution, bool transitive, ...)
```

Requires loading a live `Solution` via MSBuildWorkspace. Consider caching the last-opened workspace (invalidate on snapshot version change). Two sub-queries: (1) interface member implementations, (2) derived classes/abstract overrides. PathAllowlist on source spans. Output formats: json/markdown/tron consistent with existing tools. Pagination: offset/limit.

### `doc_coverage`

Walk `SymbolGraphSnapshot.Nodes` filtering `NodeKind.Real` only (exclude stubs). For each node, check `node.DocComment` non-null and non-empty. Group by project → namespace → type. Return coverage ratio at each level. Add `--minKind` filter to exclude parameters/fields if too noisy. Optional: flag symbols with `DocComment` that only contains `<inheritdoc/>` as "undocumented" (configurably).

### Startup Validation Pattern

```csharp
// DocAgentServerOptions — add data annotations
[Required(AllowEmptyStrings = false)]
public string? ArtifactsDir { get; set; }

// Program.cs / service registration
services.AddOptions<DocAgentServerOptions>()
    .Bind(configuration.GetSection("DocAgent"))
    .ValidateDataAnnotations()
    .ValidateOnStart();  // .NET 6+ — throws on startup if validation fails

// IHostedService for filesystem check
// Verify ArtifactsDir exists and is writable before first tool call
```

### Rate Limiting

```csharp
// DocAgentServerOptions additions
public RateLimitOptions RateLimit { get; set; } = new();

public sealed class RateLimitOptions {
    public bool Enabled { get; set; } = false;
    public int CallsPerMinute { get; set; } = 120;
    public int BurstSize { get; set; } = 20;
}

// Tool method entry pattern
// Inject TokenBucketRateLimiter; call TryAcquire()
// Return: { "error": "rate_limited", "retryAfterMs": N } if denied
```

---

## Sources

- [MCP Pagination Specification (2025-03-26)](https://modelcontextprotocol.io/specification/2025-03-26/server/utilities/pagination) — HIGH confidence
- [SymbolFinder.FindImplementationsAsync — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder.findimplementationsasync?view=roslyn-dotnet-4.9.0) — HIGH confidence
- [SymbolFinder.FindDerivedClassesAsync — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.findsymbols.symbolfinder.findderivedclassesasync?view=roslyn-dotnet-4.9.0) — HIGH confidence
- [Microsoft.CodeAnalysis 4.14.0 on NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis/4.14.0) — HIGH confidence
- [.NET IOptions ValidateOnStart pattern — Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0) — HIGH confidence
- [Rate Limiting for .NET — .NET Blog](https://devblogs.microsoft.com/dotnet/announcing-rate-limiting-for-dotnet/) — HIGH confidence
- [MCP In .NET Pagination Docs](https://mcpindotnet.github.io/docs/concepts/architecture-overview/layers/data-layer/utility-features/pagination/) — MEDIUM confidence (community docs)
- Existing codebase: `DocTools.cs`, `DocAgentServerOptions.cs`, `PROJECT.md` — HIGH confidence (direct inspection)

---
*Feature research for: DocAgentFramework v1.5 Robustness milestone*
*Researched: 2026-03-04*
