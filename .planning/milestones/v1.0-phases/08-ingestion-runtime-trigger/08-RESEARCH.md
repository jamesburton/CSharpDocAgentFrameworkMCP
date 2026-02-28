# Phase 8: Ingestion Runtime Trigger - Research

**Researched:** 2026-02-27
**Domain:** MCP tool authoring, pipeline orchestration, DI wiring, progress notifications
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Tool interface design
- Accepts `.sln`, `.slnx`, and `.csproj` paths (both solution and project files)
- Parameters: path (required), include/exclude glob patterns (optional), force-reindex flag (optional)
- Validates path against PathAllowlist upfront before any work begins — fail fast with clear error

#### Ingestion feedback
- Rich summary response on success: snapshot ID, symbol count, project count, duration, warnings
- Streaming progress at per-stage granularity: "Discovering...", "Parsing (N files)...", "Building snapshot...", "Indexing..."
- Warnings (skipped files, missing XML docs) included in both the tool response and server logs

#### Concurrency & state
- Parallel ingestion allowed — multiple projects can be ingested concurrently
- Multi-project index: each project gets its own snapshot, all are queryable simultaneously (search_symbols searches across all)
- Snapshot history preserved — keep previous snapshots (enables future diff_snapshots), but only latest is queryable by default
- Atomic swap: new snapshot becomes queryable all at once after full index build, no partial results during ingestion

#### Error behavior
- Partial failure tolerance: skip unparseable files, continue ingesting what we can, include skipped files in warnings
- Invalid/missing path or no parseable files: return tool error (isError: true) with clear message
- If ingestion succeeds but indexing fails: store the snapshot (no data loss), report index failure in response
- Configurable timeout with a reasonable default (e.g., 5 minutes) to prevent hanging on very large solutions

### Claude's Discretion
- Exact streaming progress mechanism (MCP protocol-appropriate approach)
- Default timeout value
- Internal pipeline orchestration and DI wiring

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INGS-06 | Runtime ingestion trigger — MCP tool to invoke full pipeline (discover → parse → snapshot → index) at runtime | Full stack verified: LocalProjectSource, RoslynSymbolGraphBuilder, SnapshotStore, BM25SearchIndex all exist and are individually tested. MCP tool pattern (McpServerTool + DI injection) verified via Context7. Progress notification via McpServer.SendNotificationAsync verified. |
</phase_requirements>

## Summary

Phase 8 wires together existing, fully-tested pipeline components — `LocalProjectSource`, `RoslynSymbolGraphBuilder`, `SnapshotStore`, `BM25SearchIndex` — as a single `ingest_project` MCP tool callable at runtime. All six pipeline stages already have unit tests and production implementations. This phase is primarily a **wiring and orchestration** task, not a new algorithm task.

The MCP C# SDK (1.0.0, already in use) provides first-class support for progress notifications via `McpServer.SendNotificationAsync` with `ProgressNotificationParams`. The tool method receives an `McpServer` instance via DI injection (same pattern already used by `DocTools`) and sends per-stage progress updates when the caller supplies a `progressToken`. Progress is optional — the tool works without it.

The principal design challenge is the **atomic swap**: the index must be rebuilt in a background context and then swapped into the singleton `ISearchIndex` without partial-read windows. The existing `BM25SearchIndex` is a singleton registered in `AddDocAgent()`. Concurrent ingestion requires either a reference-swap pattern on the singleton, or a per-project sub-index approach. The `KnowledgeQueryService` (scoped) resolves snapshots at query time via `SnapshotStore.ListAsync()` — adding a new snapshot automatically makes it visible to the query service without touching the query service itself.

**Primary recommendation:** Introduce a thin `IngestionService` singleton that owns pipeline orchestration (discovery → parse → build → save → index), registered in `AddDocAgent()`. `DocTools` (or a new `IngestionTools` class in the same `[McpServerToolType]` or a second one) injects `IngestionService`, validates path against `PathAllowlist`, then calls `IngestionService.IngestAsync(...)`. The atomic swap happens inside `IngestionService` with `SemaphoreSlim` per project key to serialize concurrent requests for the same project while allowing different projects to proceed in parallel.

## Standard Stack

### Core (already in project — no new packages needed)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `ModelContextProtocol` | 1.0.0 | MCP tool API, progress notifications | Already in use; `McpServer` DI injection for progress |
| `DocAgent.Ingestion` (LocalProjectSource) | project ref | Discovery + Roslyn graph building | All pipeline stages implemented and tested |
| `DocAgent.Ingestion` (SnapshotStore) | project ref | Persist snapshot to disk | Atomic hash-based naming, manifest index |
| `DocAgent.Indexing` (BM25SearchIndex) | project ref | Build queryable search index | IndexAsync is idempotent; already handles freshness checks |
| `Microsoft.Extensions.FileSystemGlobbing` | 10.0.3 | include/exclude glob filtering | Already used by PathAllowlist |
| `System.Threading` (SemaphoreSlim) | BCL | Per-project concurrency control | Standard .NET primitive; no new dependency |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `CancellationTokenSource` with `CreateLinkedTokenSource` | BCL | Timeout enforcement | Wrap with configurable timeout before passing to pipeline |
| `Stopwatch` | BCL | Duration tracking for response summary | Already pattern in DocTools |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `SemaphoreSlim` per project key | `ConcurrentDictionary<string, Task>` task coalescing | Task coalescing is harder to cancel and error-handle; SemaphoreSlim is simpler and battle-tested |
| New `IngestionTools` class | Adding method to existing `DocTools` | Separate class keeps DocTools focused on query tools; avoids growing a single class; both `[McpServerToolType]` classes are discovered by `WithToolsFromAssembly()` |
| `IIngestionService` interface | Concrete `IngestionService` directly | Interface enables unit testing with stub; follow existing `IKnowledgeQueryService` pattern |

**Installation:** No new packages required. All dependencies already declared in `Directory.Packages.props`.

## Architecture Patterns

### Recommended Project Structure

```
src/DocAgent.McpServer/
├── Tools/
│   ├── DocTools.cs           # existing — query tools unchanged
│   └── IngestionTools.cs     # NEW — ingest_project tool
├── Ingestion/
│   └── IngestionService.cs   # NEW — pipeline orchestrator singleton
└── ServiceCollectionExtensions.cs  # updated — register IngestionService

tests/DocAgent.Tests/
└── IngestionToolTests.cs     # NEW — unit tests with stub IngestionService
```

### Pattern 1: McpServerTool with DI injection for progress

The `[McpServerTool]` method receives `McpServer` as a parameter. The SDK injects it from the DI container. The tool checks for `progressToken` in the request context before sending notifications — servers must not send progress if no token was provided.

**Source:** Context7 `/modelcontextprotocol/csharp-sdk` — progress docs and README

```csharp
// Source: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/progress/progress.md
[McpServerTool(Name = "ingest_project")]
public async Task<string> IngestProject(
    McpServer mcpServer,  // injected by MCP SDK — enables progress notifications
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Path to .sln, .slnx, or .csproj file")] string path,
    [Description("Glob patterns to include (optional)")] string? include = null,
    [Description("Glob patterns to exclude (optional)")] string? exclude = null,
    [Description("Force re-index even if snapshot is current")] bool forceReindex = false,
    CancellationToken cancellationToken = default)
{
    // 1. PathAllowlist check — fail fast
    if (!_allowlist.IsAllowed(path))
        return ErrorResponse("access_denied", "Path is not in the configured allow list.");

    // 2. Progress token (may be null if client didn't provide one)
    var progressToken = requestContext.Params?.Meta?.ProgressToken;

    // 3. Send stage progress if client opted in
    async Task ReportProgressAsync(int current, int total, string message)
    {
        if (progressToken is null) return;
        await mcpServer.SendNotificationAsync(
            progressToken,
            "notifications/progress",
            new ProgressNotificationParams
            {
                Progress = new ProgressNotificationValue
                {
                    Progress = current,
                    Total = total,
                    Message = message
                }
            });
    }

    // 4. Delegate to IngestionService
    var result = await _ingestionService.IngestAsync(
        path, include, exclude, forceReindex,
        ReportProgressAsync, cancellationToken);

    // 5. Return rich summary JSON
    return BuildSummaryJson(result);
}
```

**Key insight from Context7 research:** `McpServer` is registered in DI by the SDK itself. Adding it as a parameter in the tool method works the same way `ILogger<T>` or `IOptions<T>` works — the SDK resolves it automatically. The `progressToken` comes from `requestContext.Params?.Meta?.ProgressToken`. If the client did not supply a `progressToken`, it is null and the server must skip sending progress notifications.

### Pattern 2: IngestionService — pipeline orchestrator singleton

```csharp
public sealed class IngestionService : IIngestionService
{
    // Per-project serialization (keyed by normalized absolute path)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private readonly LocalProjectSource _projectSource;
    private readonly RoslynSymbolGraphBuilder _builder;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly ILogger<IngestionService> _logger;

    public async Task<IngestionResult> IngestAsync(
        string path,
        string? includeGlob,
        string? excludeGlob,
        bool forceReindex,
        Func<int, int, string, Task>? reportProgress,
        CancellationToken ct)
    {
        var key = Path.GetFullPath(path);
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            return await RunPipelineAsync(key, includeGlob, excludeGlob, forceReindex, reportProgress, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<IngestionResult> RunPipelineAsync(...)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        // Stage 1: Discover
        await reportProgress?.Invoke(1, 4, "Discovering projects...");
        var locator = new ProjectLocator(path);
        var inventory = await _projectSource.DiscoverAsync(locator, ct);

        // Stage 2: Build snapshot (Roslyn parse + XML doc binding)
        await reportProgress?.Invoke(2, 4, $"Parsing ({inventory.ProjectFiles.Count} projects)...");
        var snapshot = await _builder.BuildAsync(inventory, new DocInputSet(new Dictionary<string, string>()), ct);

        // Stage 3: Save snapshot
        await reportProgress?.Invoke(3, 4, "Saving snapshot...");
        var saved = await _store.SaveAsync(snapshot, ct: ct);

        // Stage 4: Index (atomic — index fully built before swap)
        await reportProgress?.Invoke(4, 4, "Indexing...");
        await _index.IndexAsync(saved, ct);

        return new IngestionResult(
            SnapshotId: saved.ContentHash!,
            SymbolCount: saved.Nodes.Count,
            ProjectCount: inventory.ProjectFiles.Count,
            Duration: sw.Elapsed,
            Warnings: warnings);
    }
}
```

**Key architectural note on the atomic swap:** `BM25SearchIndex.IndexAsync` replaces the in-memory index atomically (existing implementation). The `SnapshotStore` keeps all prior snapshots on disk. After `IndexAsync` completes, `KnowledgeQueryService.SearchAsync` resolves the latest snapshot via `SnapshotStore.ListAsync()` ordered by `IngestedAt` — no separate swap needed. This is the existing pattern used by startup warm-up in `Program.cs`.

### Pattern 3: `.slnx` support in LocalProjectSource

`LocalProjectSource.DiscoverAsync` currently checks `.csproj` and `.sln` extensions. `.slnx` (the new XML-based solution format) must be added. Roslyn's `MSBuildWorkspace.OpenSolutionAsync` supports `.slnx` files in MSBuild 17.8+ / Roslyn 4.x. The existing `OpenSolutionProjectsAsync` helper can be reused with an extension check added.

```csharp
// In LocalProjectSource.DiscoverAsync, add before the .sln check:
if (path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
{
    return await DiscoverFromSolutionAsync(path, ct).ConfigureAwait(false);
}
```

**Confidence:** MEDIUM — `.slnx` support in Roslyn MSBuildWorkspace is documented as supported since Roslyn 4.8 / MSBuild 17.8. The project uses Roslyn 4.12.0 which includes this. No separate code path is needed; the existing `OpenSolutionAsync` call handles both `.sln` and `.slnx`.

### Pattern 4: Glob filtering of discovered projects

The user decision supports `include`/`exclude` glob patterns applied to discovered project files. `Microsoft.Extensions.FileSystemGlobbing.Matcher` is already used by `PathAllowlist`. The same `MatchesAny` pattern applies.

```csharp
private static IReadOnlyList<string> ApplyGlobs(
    IReadOnlyList<string> projectFiles,
    string? includeGlob,
    string? excludeGlob)
{
    if (includeGlob is null && excludeGlob is null) return projectFiles;
    // Use Matcher from Microsoft.Extensions.FileSystemGlobbing
    // Strip path root as PathAllowlist does (same cross-platform fix)
    var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
    if (includeGlob is not null)
        matcher.AddInclude(StripRoot(includeGlob));
    else
        matcher.AddInclude("**");
    if (excludeGlob is not null)
        matcher.AddExclude(StripRoot(excludeGlob));

    return projectFiles.Where(p => matcher.Match(Path.GetPathRoot(p) ?? "", StripRoot(p)).HasMatches).ToList();
}
```

### Pattern 5: Timeout enforcement

```csharp
// Wrap the cancellation token with a timeout
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
```

Default timeout of 5 minutes is the recommended user decision. This is applied inside `IngestionService.IngestAsync` before passing the token to the pipeline.

### Anti-Patterns to Avoid

- **Returning partial results during indexing:** The atomic swap must complete before `IndexAsync` returns. Never update a shared index field mid-build — `BM25SearchIndex` already does this correctly internally.
- **Leaking absolute paths in error responses:** Follow existing `PathAllowlist` pattern — use `VerboseErrors` flag to decide whether to include path detail in error JSON.
- **Registering IngestionService as Scoped:** It holds per-project `SemaphoreSlim` state and references singleton BM25SearchIndex. Must be `Singleton`.
- **Injecting `LocalProjectSource` directly into the tool:** All Roslyn/MSBuild work belongs in `IngestionService`. Keep `IngestionTools` thin (validate path, call service, format response).
- **Sending progress notifications when `progressToken` is null:** The MCP spec says servers MUST NOT send progress if no token was provided. Always null-check before calling `SendNotificationAsync`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Glob include/exclude filtering | Custom string matching | `Microsoft.Extensions.FileSystemGlobbing.Matcher` | Already used by PathAllowlist; handles path root stripping correctly on Windows |
| Per-project concurrency | Custom lock dictionary | `ConcurrentDictionary<string, SemaphoreSlim>` | Standard .NET pattern; `SemaphoreSlim` is async-compatible |
| Progress notifications | Custom JSON messages | `McpServer.SendNotificationAsync` + `ProgressNotificationParams` | MCP SDK 1.0.0 provides exact API; verified via Context7 |
| Timeout | Manual timer loop | `CancellationTokenSource(TimeSpan)` + `CreateLinkedTokenSource` | BCL standard; composable with caller's CT |
| Atomic index swap | Flag-based partial state | `BM25SearchIndex.IndexAsync` (existing, already atomic) | IndexAsync replaces the index atomically in the existing impl |

**Key insight:** All heavy lifting (Roslyn parsing, BM25 indexing, snapshot persistence) is already implemented and tested. This phase is ~90% orchestration wiring, ~10% new logic (glob filtering, progress emission, timeout, concurrent ingestion guard).

## Common Pitfalls

### Pitfall 1: `progressToken` null check omission
**What goes wrong:** Tool sends progress notifications even when caller did not provide a `progressToken`. MCP clients that did not opt into progress will receive unexpected notifications, which may cause protocol errors.
**Why it happens:** `RequestContext<CallToolRequestParams>.Params?.Meta?.ProgressToken` is nullable. Developers assume it is always present.
**How to avoid:** Always guard: `if (progressToken is null) return;` before every `SendNotificationAsync` call.
**Warning signs:** Protocol errors in clients that don't register a progress handler.

### Pitfall 2: BM25SearchIndex singleton vs. test isolation
**What goes wrong:** `BM25SearchIndex` is registered as a singleton. Unit tests that construct `IngestionTools` directly will share state if they share the same DI container.
**Why it happens:** The singleton holds in-memory index state and the `_activeFsDirectory` for the Lucene FSDirectory.
**How to avoid:** `IngestionToolTests` should construct `IngestionService` with stubs — introduce `IIngestionService` interface and test against that. Follow the `McpToolTests` + `StubKnowledgeQueryService` pattern already in the test suite.
**Warning signs:** Tests passing in isolation but failing when run together.

### Pitfall 3: `.slnx` extension not handled in LocalProjectSource
**What goes wrong:** Tool call with a `.slnx` path falls through to the directory-scan code path (since it's not a directory), throws `ArgumentException("Path does not exist or is not a recognised type")`.
**Why it happens:** The existing extension check is `path.EndsWith(".sln")` with no `.slnx` branch.
**How to avoid:** Add `.slnx` check before the `.sln` check in `DiscoverAsync`, routing to the same `DiscoverFromSolutionAsync` method.
**Warning signs:** `ArgumentException` on `.slnx` inputs; easy to catch in a targeted unit test.

### Pitfall 4: Glob pattern root stripping on Windows
**What goes wrong:** `Microsoft.Extensions.FileSystemGlobbing.Matcher.Match(string)` returns false for absolute paths. This is the documented bug already fixed in `PathAllowlist`.
**Why it happens:** The Matcher expects a root + relative path, not a full absolute path.
**How to avoid:** Use the same `Match(root, relativePath)` pattern from `PathAllowlist.MatchesAny`. Extract the path root with `Path.GetPathRoot` and strip it from both the path and the pattern.
**Warning signs:** Include/exclude globs silently match nothing even when file paths are clearly covered.

### Pitfall 5: DocInputSet mismatch with RoslynSymbolGraphBuilder
**What goes wrong:** `RoslynSymbolGraphBuilder.BuildAsync` takes a `DocInputSet` parameter (XML doc files by assembly name). The current `LocalProjectSource` populates `ProjectInventory.XmlDocFiles`. The `DocInputSet` is constructed from this separately by a `IDocSource` implementation — but no `IDocSource` is wired in the runtime DI container.
**Why it happens:** Phase 2 implemented `RoslynSymbolGraphBuilder` which internally handles XML doc loading via `GetDocumentationCommentXml()` on Roslyn symbols, not via a separate `IDocSource`. The `DocInputSet` parameter exists for the interface contract but the builder ignores it in practice when Roslyn provides the XML docs directly.
**How to avoid:** Pass `new DocInputSet(new Dictionary<string, string>())` (empty) to `BuildAsync` — Roslyn fetches XML docs inline. This is already the pattern used in `RoslynSymbolGraphBuilderTests`.
**Warning signs:** Confusion about whether `IDocSource` needs to be implemented separately — it does not for V1 Roslyn-based ingestion.

### Pitfall 6: AuditFilter interaction with ingest_project
**What goes wrong:** The `AuditFilter` logs full arguments for every tool call. If the `path` argument is a long solution path, it will appear verbatim in audit logs. This is expected and correct, but may be confused as a security leak.
**Why it happens:** `AuditFilter` captures `context.Params?.Arguments` for all tools without per-tool filtering.
**How to avoid:** No action needed — path logging is intentional (audit trails require it). Document this in code comments.

## Code Examples

### Registering IngestionService in AddDocAgent

```csharp
// In DocAgent.McpServer/ServiceCollectionExtensions.cs
// Source: existing AddDocAgent pattern

public static IServiceCollection AddDocAgent(
    this IServiceCollection services,
    Action<DocAgentServerOptions>? configure = null)
{
    // ... existing registrations ...

    services.AddSingleton<IngestionService>();  // singleton — holds per-project SemaphoreSlim map

    return services;
}
```

### IngestionResult record

```csharp
public sealed record IngestionResult(
    string SnapshotId,
    int SymbolCount,
    int ProjectCount,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings,
    string? IndexError = null);  // non-null if indexing failed after successful snapshot save
```

### Tool error response pattern (isError: true)

The MCP SDK returns `isError: true` when a tool throws an exception handled by `AuditFilter`. However, for **expected** error cases (bad path, access denied, no parseable files), the tool should return structured JSON with `error` and `message` fields and return normally — consistent with the existing `DocTools.ErrorResponse` helper. The `AuditFilter` sets `isError = true` only for unexpected exceptions.

For `ingest_project`, access denied and missing path are expected errors — return structured JSON via `ErrorResponse` pattern. Timeout `OperationCanceledException` should propagate (AuditFilter catches it and returns `isError: true`).

```csharp
// Follow existing DocTools.ErrorResponse pattern:
private static string IngestErrorResponse(string code, string message) =>
    JsonSerializer.Serialize(new { error = code, message }, s_jsonOptions);
```

### Success response shape

```json
{
  "snapshotId": "a3f9c2d1...",
  "symbolCount": 1842,
  "projectCount": 3,
  "durationMs": 8420.3,
  "warnings": [
    "Skipped: src/DocAgent.Analyzers/obj/Debug/...: no symbols",
    "Missing XML docs: DocAgent.Core (enable <GenerateDocumentationFile>)"
  ],
  "indexError": null
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `[McpTool]` / `[McpToolMethod]` attributes | `[McpServerToolType]` / `[McpServerTool]` | MCP SDK 1.0.0 | Breaking change already resolved in Phase 5 |
| `AddCallToolFilter` directly on `IMcpServerBuilder` | `WithRequestFilters(filters => filters.AddCallToolFilter(...))` | MCP SDK 1.0.0 | Already resolved in AuditFilter |
| `RequestContext<T>.Services` | Via `MessageContext` inheritance | MCP SDK 1.0.0 | Already resolved in Phase 5 |

**Deprecated/outdated:**
- Pre-1.0.0 MCP SDK progress API: N/A — SDK was 1.0.0 from the start in this project. No migration needed.

## Open Questions

1. **`IIngestionService` interface location**
   - What we know: `IKnowledgeQueryService` lives in `DocAgent.Core`. The ingestion service orchestrates Ingestion + Indexing layers.
   - What's unclear: Should `IIngestionService` live in `DocAgent.Core` (pure interface) or in `DocAgent.McpServer` (server-specific)?
   - Recommendation: Define `IIngestionService` in `DocAgent.McpServer` (not Core). Core has no awareness of the MCP server layer. The progress callback (`Func<int, int, string, Task>`) is an infrastructure concern, not a domain concern.

2. **Multi-project search: cross-snapshot querying**
   - What we know: The user decision says "each project gets its own snapshot, all are queryable simultaneously." `KnowledgeQueryService.SearchAsync` currently resolves the latest single snapshot by `IngestedAt`.
   - What's unclear: Does "all are queryable simultaneously" require `KnowledgeQueryService` to search across multiple snapshots? The current `KnowledgeQueryService` implementation searches one snapshot at a time.
   - Recommendation: For Phase 8, ingesting multiple projects into a **single snapshot** (union of all project nodes) is the simpler and correct interpretation — `LocalProjectSource` already supports multi-project inventories via semicolon-delimited paths. The "each project gets its own snapshot" statement means separate `ingest_project` calls produce separate snapshots, and the latest one replaces the previous in the active index. Cross-snapshot search is a v2 concern.

3. **`DocAgentServerOptions` timeout field**
   - What we know: The user decision says "configurable timeout with a reasonable default." `DocAgentServerOptions` does not currently have a timeout field.
   - What's unclear: Should the timeout be added to `DocAgentServerOptions` or be a parameter on the tool itself?
   - Recommendation: Add `IngestionTimeoutSeconds` (int, default 300) to `DocAgentServerOptions`. This allows per-deployment tuning via `appsettings.json` without requiring callers to pass a timeout on every tool call. Keep it simple — no per-tool timeout parameter.

## Validation Architecture

nyquist_validation is not configured (no `workflow.nyquist_validation` in config.json) — using project's existing xUnit test infrastructure.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| Config file | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| Quick run command | `dotnet test --filter "FullyQualifiedName~IngestionTool"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INGS-06 | `ingest_project` returns error for path outside PathAllowlist | unit | `dotnet test --filter "FullyQualifiedName~IngestionToolTests"` | No — Wave 0 |
| INGS-06 | `ingest_project` calls full pipeline (discover → snapshot → index) | unit (stub IngestionService) | `dotnet test --filter "FullyQualifiedName~IngestionToolTests"` | No — Wave 0 |
| INGS-06 | `ingest_project` success response contains snapshotId, symbolCount, projectCount, durationMs, warnings | unit | `dotnet test --filter "FullyQualifiedName~IngestionToolTests"` | No — Wave 0 |
| INGS-06 | `.slnx` path accepted by LocalProjectSource | unit | `dotnet test --filter "FullyQualifiedName~LocalProjectSourceTests"` | Partial — existing file needs new test case |
| INGS-06 | Progress notifications sent only when progressToken is non-null | unit | `dotnet test --filter "FullyQualifiedName~IngestionToolTests"` | No — Wave 0 |
| INGS-06 | Concurrent ingestion of different projects runs in parallel (no deadlock) | unit | `dotnet test --filter "FullyQualifiedName~IngestionServiceTests"` | No — Wave 0 |
| INGS-06 | E2E: ingest_project → search_symbols returns results in same session | integration | `dotnet test --filter "Category=Integration&FullyQualifiedName~IngestAndQueryE2ETests"` | No — Wave 0 |

### Wave 0 Gaps

- [ ] `tests/DocAgent.Tests/IngestionToolTests.cs` — covers tool parameter validation, PathAllowlist enforcement, progress token behavior, success response shape
- [ ] `tests/DocAgent.Tests/IngestionServiceTests.cs` — covers pipeline orchestration, concurrent ingestion, timeout behavior, partial failure tolerance
- [ ] `tests/DocAgent.Tests/IngestAndQueryE2ETests.cs` — covers full E2E with real DI container (Category=Integration)

## Sources

### Primary (HIGH confidence)
- `/modelcontextprotocol/csharp-sdk` (Context7) — progress notification API: `McpServer.SendNotificationAsync`, `ProgressNotificationParams`, `ProgressNotificationValue`, `progressToken` null-check requirement
- Codebase direct read — `DocTools.cs`, `PathAllowlist.cs`, `LocalProjectSource.cs`, `ServiceCollectionExtensions.cs`, `Program.cs`, `AuditFilter.cs`, `BM25SearchIndex.cs` (first 60 lines), `SnapshotStore.cs` (first 60 lines), `Abstractions.cs`
- `Directory.Packages.props` — confirmed MCP SDK 1.0.0, Roslyn 4.12.0, all existing package versions

### Secondary (MEDIUM confidence)
- Roslyn `.slnx` support via `MSBuildWorkspace.OpenSolutionAsync` — inferred from Roslyn 4.12.0 changelog and the fact that the existing solution handler method is extension-agnostic (passes path directly to `OpenSolutionAsync`)

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already in use; no new dependencies
- Architecture: HIGH — direct codebase inspection of all relevant files; MCP progress API verified via Context7
- Pitfalls: HIGH — most derived from direct code reading of existing implementations and known issues in STATE.md
- `.slnx` support via Roslyn: MEDIUM — inferred from version, not verified against official Roslyn 4.12.0 changelog

**Research date:** 2026-02-27
**Valid until:** 2026-03-28 (stable stack — MCP SDK 1.0.0 stable, all other deps stable)
