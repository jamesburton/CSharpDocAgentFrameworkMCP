# Phase 7: Runtime Integration Wiring - Research

**Researched:** 2026-02-27
**Domain:** .NET DI wiring, IOptions pattern, IServiceCollection extensions, async-enumerable graph traversal, xUnit E2E integration testing
**Confidence:** HIGH — all findings derived from direct codebase inspection; no speculative claims

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**DI Registration Strategy**
- Single `AddDocAgent()` extension method on `IServiceCollection` in `DocAgent.McpServer` project
- `SnapshotStore` and `BM25SearchIndex` registered as **singletons** (expensive to build, immutable)
- `IKnowledgeQueryService` registered as **scoped** (per-request)
- Support both `IOptions<DocAgentServerOptions>` pattern AND `Action<DocAgentServerOptions>` delegate for configuration

**ArtifactsDir Configuration**
- Three config sources with priority: CLI argument > environment variable (`DOCAGENT_ARTIFACTS_DIR`) > `appsettings.json` (`DocAgent:ArtifactsDir`)
- Default value: `./artifacts` (relative to working directory) if nothing configured
- **Auto-create** directory if it doesn't exist — fail only on permission errors
- **Eager startup validation** — validate and create directory during DI registration, fail fast if problems

**GetReferencesAsync Behavior**
- Return **all edge types** (inherits, implements, calls, references, contains)
- Return **bidirectional** edges — both incoming (who references me) and outgoing (what I reference)
- Accept **optional edge type filter** parameter: `GetReferencesAsync(symbolId, edgeTypes?)` — defaults to all
- **Throw `SymbolNotFoundException`** when the symbol ID doesn't exist in the graph (distinguish from "exists but no references")

**E2E Integration Test Design**
- Use a **synthetic minimal .csproj** with known types as test input — fast, deterministic, no external dependencies
- **Both** in-process DI test (build real container, resolve services, call pipeline) AND subprocess stdio smoke test
- **All 5 MCP tools** must return non-error responses: `search_symbols`, `get_symbol`, `get_references`, `diff_snapshots`, `explain_project`
- Use **temp directory** for artifacts, cleaned up after each test run — no stale state, parallel-safe

### Claude's Discretion
- Exact structure of the synthetic test project (how many types, what relationships)
- `AddDocAgent()` internal implementation details (registration order, factory patterns)
- Subprocess smoke test transport details (how to start/stop the server in test)
- Error message formatting for startup validation failures

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MCPS-03 | `get_references` MCP tool returns real edges from the snapshot graph | GetReferencesAsync real implementation section; edge data is already in `SymbolGraphSnapshot.Edges` |
| QURY-01 | `IKnowledgeQueryService` facade wired to `ISearchIndex` + `SnapshotStore` | DI registration: `KnowledgeQueryService` registered as scoped, depends on `ISearchIndex` (BM25SearchIndex singleton) and `SnapshotStore` singleton |
| INDX-01 | BM25 search index replaces `InMemorySearchIndex` | `BM25SearchIndex` registered as `ISearchIndex` singleton; `InMemorySearchIndex` removed from production registrations (can stay in codebase as test utility) |
| INDX-03 | Index persistence alongside snapshots | `ArtifactsDir` flows into both `SnapshotStore` and `BM25SearchIndex` constructor; both use the same directory |
| INGS-04 | `SnapshotStore` writes/reads versioned snapshots | `SnapshotStore` registered as singleton with `ArtifactsDir` resolved from config |
| MCPS-01 | `search_symbols` MCP tool returns real results | Satisfied once DI is wired — `DocTools` already uses `IKnowledgeQueryService` |
| MCPS-02 | `get_symbol` MCP tool returns real results | Same — DI wiring closes this gap |
| MCPS-04 | `diff_snapshots` MCP tool returns real results | Same — DI wiring closes this gap |
| MCPS-05 | `explain_project` MCP tool returns real results | Same — DI wiring closes this gap |
</phase_requirements>

---

## Summary

Phase 7 is pure wiring — no new domain logic. Three gaps from the v1.0 audit must be closed:

1. **DI gap**: `IKnowledgeQueryService`, `ISearchIndex` (BM25SearchIndex), and `SnapshotStore` are not registered in `Program.cs`. `DocTools` injects `IKnowledgeQueryService` but nothing provides it — the server crashes at runtime when any tool is invoked.

2. **Configuration gap**: `DocAgentServerOptions` has no `ArtifactsDir` property. Both `SnapshotStore` and `BM25SearchIndex` require a filesystem path. Without this property the path cannot be passed from config to the implementations.

3. **GetReferencesAsync stub**: `KnowledgeQueryService.GetReferencesAsync` is `yield break`. The graph edges that `RoslynSymbolGraphBuilder` already produces (Contains, Inherits, Implements, Calls, References, Overrides, Returns) are sitting in `SymbolGraphSnapshot.Edges` and just need to be queried bidirectionally.

The E2E integration test is the correctness proof: synthetic `.csproj` → build container → call all 5 tools → assert non-error JSON responses. The subprocess smoke test (already demonstrated in `StdoutContaminationTests`) shows the existing pattern to follow.

**Primary recommendation:** Wire `AddDocAgent()` extension method in `Program.cs`, add `ArtifactsDir` to `DocAgentServerOptions`, implement `GetReferencesAsync` by filtering `snapshot.Edges`, write the E2E test, and delete the `InMemorySearchIndex` from production concerns.

---

## Standard Stack

### Core (already in use — no new packages needed)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| `Microsoft.Extensions.DependencyInjection` | 10.0.x (transitive via Hosting) | DI container | Already in `Microsoft.Extensions.Hosting` dependency |
| `Microsoft.Extensions.Options` | 10.0.x (transitive) | `IOptions<T>` pattern | Already used in `DocAgentServerOptions` |
| `Microsoft.Extensions.Hosting` | `10.0.0-preview.2.25163.2` | `Host.CreateApplicationBuilder` | Already pinned in `Directory.Packages.props` |

No new NuGet packages are required. All needed libraries are already referenced.

### Supporting (test side)

| Library | Version | Purpose | Notes |
|---------|---------|---------|-------|
| `xunit` | 2.9.3 | Test framework | Already in `DocAgent.Tests.csproj` |
| `FluentAssertions` | 6.12.1 | Assertion library | Already referenced |
| `System.Diagnostics.Process` | BCL | Subprocess smoke test | No new package — used in `StdoutContaminationTests` already |

---

## Architecture Patterns

### Pattern 1: IServiceCollection Extension Method (`AddDocAgent`)

The standard .NET pattern for library DI registration. Keeps `Program.cs` clean and testable.

```csharp
// Source: DocAgent.McpServer project — new file ServiceCollectionExtensions.cs
public static class DocAgentServiceCollectionExtensions
{
    public static IServiceCollection AddDocAgent(
        this IServiceCollection services,
        Action<DocAgentServerOptions>? configure = null)
    {
        // Apply optional delegate configuration
        if (configure is not null)
            services.Configure(configure);

        // Register using a factory to resolve ArtifactsDir from IOptions
        services.AddSingleton<SnapshotStore>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;
            var dir = ResolveAndCreateArtifactsDir(opts.ArtifactsDir);
            return new SnapshotStore(dir);
        });

        services.AddSingleton<ISearchIndex>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;
            var dir = ResolveAndCreateArtifactsDir(opts.ArtifactsDir);
            return new BM25SearchIndex(dir);
        });

        services.AddScoped<IKnowledgeQueryService, KnowledgeQueryService>();

        return services;
    }

    private static string ResolveAndCreateArtifactsDir(string? configuredPath)
    {
        var dir = string.IsNullOrWhiteSpace(configuredPath) ? "./artifacts" : configuredPath;
        Directory.CreateDirectory(dir);  // auto-create; throws on permission errors (eager validation)
        return dir;
    }
}
```

**Important:** Both `SnapshotStore` and `BM25SearchIndex` must receive the **same resolved path** — call `ResolveAndCreateArtifactsDir` once and cache, or use a shared registration. Do NOT compute it twice or they may diverge.

### Pattern 2: Config Priority Chain (CLI > Env > appsettings)

`Host.CreateApplicationBuilder` already reads in the standard order (appsettings.json → environment variables → command-line args) when using `builder.Configuration`. The `DocAgent:ArtifactsDir` key resolves automatically from:
- `appsettings.json` section `"DocAgent": { "ArtifactsDir": "..." }`
- Environment variable `DocAgent__ArtifactsDir` (double-underscore for nested keys in .NET config)

The user context requires the env variable to be `DOCAGENT_ARTIFACTS_DIR`. This is a custom environment variable name (not the default double-underscore convention). To support it alongside the standard .NET config chain:

```csharp
// In Program.cs, before builder.Services.Configure<...>:
var artifactsDirFromEnv = Environment.GetEnvironmentVariable("DOCAGENT_ARTIFACTS_DIR");
if (!string.IsNullOrWhiteSpace(artifactsDirFromEnv))
    builder.Configuration["DocAgent:ArtifactsDir"] = artifactsDirFromEnv;
```

This injects the env variable into the config system before `IOptions<DocAgentServerOptions>` is built, maintaining the correct priority.

### Pattern 3: GetReferencesAsync — Bidirectional Edge Traversal

`SymbolGraphSnapshot.Edges` is an `IReadOnlyList<SymbolEdge>` where each `SymbolEdge` has `From`, `To`, and `Kind`. Bidirectional means returning edges where the symbol appears as either endpoint.

```csharp
// In KnowledgeQueryService — replaces the stub
public async IAsyncEnumerable<SymbolEdge> GetReferencesAsync(
    SymbolId id,
    SymbolEdgeKind[]? edgeTypes = null,  // null = all types
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var (snapshot, _, error) = await ResolveSnapshotAsync(null, ct).ConfigureAwait(false);
    if (error is not null || snapshot is null)
        yield break;

    // Verify symbol exists in the graph
    bool symbolExists = snapshot.Nodes.Any(n => n.Id == id);
    if (!symbolExists)
        throw new SymbolNotFoundException(id);

    foreach (var edge in snapshot.Edges)
    {
        ct.ThrowIfCancellationRequested();

        // Bidirectional: include edge if symbol appears at either end
        if (edge.From != id && edge.To != id)
            continue;

        // Edge type filter (null = all)
        if (edgeTypes is not null && !edgeTypes.Contains(edge.Kind))
            continue;

        yield return edge;
    }
}
```

**Key decisions from CONTEXT.md:**
- Throw `SymbolNotFoundException` (must define this exception in Core or Indexing) when symbol not in graph
- `edgeTypes` is an optional filter parameter (null = all)
- Return both incoming and outgoing edges

**Interface change required:** The current `IKnowledgeQueryService` signature is:
```csharp
IAsyncEnumerable<SymbolEdge> GetReferencesAsync(SymbolId id, CancellationToken ct = default);
```
Adding `edgeTypes?` changes the interface. The `DocTools.GetReferences` MCP tool currently passes no filter — it will still work by passing `null`. The `StubQueryService` in `McpIntegrationTests` also needs updating if the signature changes. Evaluate whether to add the parameter now or keep the interface minimal and implement filtering only inside `GetReferencesAsync` without exposing it to the interface. RECOMMENDATION: Add `edgeTypes` only to `KnowledgeQueryService` implementation (not to the interface), keeping the interface stable. The MCP tool can expose the filter when needed in a future phase.

### Pattern 4: In-Process DI E2E Test

The existing `KnowledgeQueryServiceTests` pattern is exactly what the E2E test should follow: build real objects, call real methods, assert on results. For E2E, use the actual DI container:

```csharp
// Pattern based on existing tests — build a real ServiceCollection
var services = new ServiceCollection();
services.Configure<DocAgentServerOptions>(o => o.ArtifactsDir = tempDir);
services.AddDocAgent();

var provider = services.BuildServiceProvider();
var query = provider.GetRequiredService<IKnowledgeQueryService>();

// Pre-populate: snapshot must exist for query to work
var store = provider.GetRequiredService<SnapshotStore>();
var index = (BM25SearchIndex)provider.GetRequiredService<ISearchIndex>();
// ... build synthetic snapshot, save to store, index it ...

var result = await query.SearchAsync("*");
result.Success.Should().BeTrue();
```

### Pattern 5: Subprocess E2E Smoke Test

Already demonstrated in `StdoutContaminationTests.cs`. The pattern is:
1. Start `dotnet run --project src/DocAgent.McpServer --no-build` as a subprocess
2. Write JSON-RPC frames to stdin
3. Read stdout for responses
4. Assert all 5 tool calls return non-error JSON

The `StdoutContaminationTests` already validates the server starts and responds to `initialize`. The E2E smoke test needs to also call all 5 tools and verify they return `{ "results": ... }` (not `{ "error": ... }`).

**Key consideration:** Once DI is wired, `SnapshotStore` will look for artifacts at the configured path. The subprocess test must either:
a. Configure `DOCAGENT_ARTIFACTS_DIR` env var to point to a temp dir with a pre-built snapshot, OR
b. Start the server with a path to a snapshot that was pre-built in the test setup

Option (b) is simpler for a smoke test: build a snapshot in the test's temp dir before starting the server subprocess, then pass the path via environment variable.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Config priority chain | Custom env-var parser | `builder.Configuration` + manual injection for custom var name | .NET config system already handles `appsettings.json` + double-underscore env vars; only need special handling for the custom `DOCAGENT_ARTIFACTS_DIR` name |
| DI container | Custom service locator | `IServiceCollection` / `ServiceProvider` | Already in use throughout the project |
| Directory creation with error handling | Try/catch/retry loop | `Directory.CreateDirectory()` — it's idempotent and throws on real errors | Works correctly for eager validation |
| Edge traversal data structure | Custom graph | `snapshot.Edges` (already an `IReadOnlyList<SymbolEdge>`) | All edges are already in memory; LINQ over them is sufficient for V1 |

---

## Common Pitfalls

### Pitfall 1: ArtifactsDir Resolved Twice With Different Results
**What goes wrong:** `SnapshotStore` and `BM25SearchIndex` both take a directory path in their constructors. If `ResolveAndCreateArtifactsDir` is called independently for each registration (e.g., with different relative paths that canonicalize differently), they end up pointing to different directories and the index never loads the snapshot.
**Why it happens:** Relative paths like `./artifacts` resolve relative to `Environment.CurrentDirectory`, which may differ between calls if anything changes it.
**How to avoid:** Resolve and canonicalize the path once (`Path.GetFullPath(...)`) in a shared factory before registering both services. Register a keyed singleton or a factory closure that captures the resolved path string.
**Warning signs:** Search works in unit tests (where RAMDirectory is injected directly) but returns empty results in integration test.

### Pitfall 2: IOptions Not Yet Populated When Singleton Factory Runs
**What goes wrong:** `services.AddSingleton<SnapshotStore>(sp => ...)` factory runs at first resolve time (lazy). If `IOptions<DocAgentServerOptions>` is resolved in the factory, it must already be configured. Since `Configure<T>()` runs before the factory, this is safe — but CLI argument injection must happen before `Build()` is called.
**Why it happens:** `IOptions<T>` is built from the configuration system during `services.BuildServiceProvider()`, not at registration time. CLI-injected values must be in `builder.Configuration` before `builder.Build()`.
**How to avoid:** Set `builder.Configuration["DocAgent:ArtifactsDir"]` from env variable before `builder.Services.Configure<DocAgentServerOptions>(...)` call in `Program.cs`.

### Pitfall 3: GetReferencesAsync Throws Inside async-iterator — Async Iteration Contract
**What goes wrong:** An `async IAsyncEnumerable<T>` method that throws `SymbolNotFoundException` before the first `yield` will propagate the exception to the caller's `await foreach` loop as an `InvalidOperationException` (wrapped by the iterator state machine) unless the exception type derives from a common base or is caught by the caller.
**Why it happens:** Exceptions thrown before the first `yield` in an async iterator are deferred to the first `MoveNextAsync()` call, not thrown at the `GetAsyncEnumerator()` call site.
**How to avoid:** Document this clearly. The `DocTools.GetReferences` already buffers into a `List<SymbolEdge>` with `await foreach` so exceptions propagate naturally as long as `DocTools` does not swallow them. Consider catching `SymbolNotFoundException` in `DocTools.GetReferences` and returning an error JSON response.
**Warning signs:** `get_references` tool hangs or returns empty instead of error when symbol not found.

### Pitfall 4: BM25SearchIndex Singleton Has No Data Until Indexed
**What goes wrong:** `BM25SearchIndex` registered as singleton starts with `_hasIndex = false`. The singleton is created at first resolve time but has no data. The MCP server will serve an empty index until a snapshot is explicitly indexed.
**Why it happens:** This is correct by design — the server reads from persistent Lucene indexes stored on disk. But `LoadIndexAsync` must be called at startup to load the latest snapshot from disk into the index.
**How to avoid:** At startup, after DI is built (in `Program.cs` before `RunAsync()`), resolve `SnapshotStore` and `BM25SearchIndex`, list the snapshots, and call `index.LoadIndexAsync(latestHash, snapshot)` if a snapshot exists. If none exists, the server starts with an empty index (acceptable — first run has nothing to serve).
**Warning signs:** DI test passes, but `SearchAsync` returns `SnapshotMissing` error at runtime even when artifact files exist on disk.

### Pitfall 5: Scoped IKnowledgeQueryService With Singleton Dependencies
**What goes wrong:** `KnowledgeQueryService` is scoped but depends on `ISearchIndex` (singleton) and `SnapshotStore` (singleton). In a non-HTTP host using Generic Host (as this project does), there is no implicit scope created per request. `DocTools` is resolved per tool invocation — MCP SDK creates the scope.
**Why it happens:** The MCP SDK (`ModelContextProtocol`) creates a DI scope per tool call (this is the behavior in v1.0 — verify via `AddMcpServer()` internals). Scoped services can depend on singletons safely; the reverse is not true.
**How to avoid:** Keep `KnowledgeQueryService` scoped (as decided). Ensure it only depends on singleton or transient services, not other scoped services that would cause captive dependency issues.
**Warning signs:** `InvalidOperationException: Cannot consume scoped service from singleton` at startup.

### Pitfall 6: Synthetic Test .csproj Must Be Self-Contained
**What goes wrong:** The E2E test builds a synthetic `.csproj` as Roslyn workspace input. If it references standard library types (e.g., `System.Object`) without proper SDK setup, `MSBuildWorkspace.OpenProjectAsync` fails or produces an empty compilation.
**Why it happens:** `RoslynSymbolGraphBuilder` uses `MSBuildWorkspace.Create()` which requires MSBuild to be locatable. In test environments, this requires `MSBuildLocator.RegisterDefaults()` to be called once before any workspace is opened.
**How to avoid:** Examine `RoslynSymbolGraphBuilderTests.cs` for the MSBuildLocator pattern already in use. The synthetic `.csproj` should target the same SDK version (`net10.0`) and include only types that compile cleanly without external dependencies.
**Warning signs:** `WorkspaceFailed` event fires with "Microsoft.Build.Locator has not been registered" — or compilation is null.

---

## Code Examples

### IServiceCollection Extension Method (complete pattern)

```csharp
// Source: codebase analysis + standard .NET DI extension pattern
// File: src/DocAgent.McpServer/ServiceCollectionExtensions.cs
using DocAgent.Indexing;
using DocAgent.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer;

public static class DocAgentServiceCollectionExtensions
{
    public static IServiceCollection AddDocAgent(
        this IServiceCollection services,
        Action<DocAgentServerOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure<DocAgentServerOptions>(configure);

        // Resolve artifact dir once and share between both singletons
        services.AddSingleton<string>("ArtifactsDir", sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;
            var raw = string.IsNullOrWhiteSpace(opts.ArtifactsDir) ? "./artifacts" : opts.ArtifactsDir;
            var full = Path.GetFullPath(raw);
            Directory.CreateDirectory(full);  // eager validation
            return full;
        });

        services.AddSingleton<SnapshotStore>(sp =>
            new SnapshotStore(sp.GetRequiredKeyedService<string>("ArtifactsDir")));

        services.AddSingleton<ISearchIndex>(sp =>
            new BM25SearchIndex(sp.GetRequiredKeyedService<string>("ArtifactsDir")));

        services.AddScoped<IKnowledgeQueryService, KnowledgeQueryService>();

        return services;
    }
}
```

NOTE: Keyed services (`AddSingleton<string>("ArtifactsDir", ...)`) require .NET 8+ keyed DI. This project targets .NET 10, so this is available. Alternative: capture the path in a closure variable during registration (simpler, no keyed DI needed):

```csharp
// Simpler alternative without keyed DI
public static IServiceCollection AddDocAgent(this IServiceCollection services, ...)
{
    // ...
    string? resolvedDir = null;
    string GetDir(IServiceProvider sp)
    {
        if (resolvedDir is not null) return resolvedDir;
        var opts = sp.GetRequiredService<IOptions<DocAgentServerOptions>>().Value;
        var raw = string.IsNullOrWhiteSpace(opts.ArtifactsDir) ? "./artifacts" : opts.ArtifactsDir;
        resolvedDir = Path.GetFullPath(raw);
        Directory.CreateDirectory(resolvedDir);
        return resolvedDir;
    }
    services.AddSingleton<SnapshotStore>(sp => new SnapshotStore(GetDir(sp)));
    services.AddSingleton<ISearchIndex>(sp => new BM25SearchIndex(GetDir(sp)));
    services.AddScoped<IKnowledgeQueryService, KnowledgeQueryService>();
    return services;
}
```

RECOMMENDATION: Use the closure approach — simpler, no keyed DI, thread-safe if both factories are called on the same thread (DI container builds singletons lazily but synchronously).

### Program.cs After Wiring

```csharp
// src/DocAgent.McpServer/Program.cs — after Phase 7
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o =>
{
    o.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Inject custom env var into config system (CLI > DOCAGENT_ARTIFACTS_DIR > appsettings.json)
var artifactsDirFromEnv = Environment.GetEnvironmentVariable("DOCAGENT_ARTIFACTS_DIR");
if (!string.IsNullOrWhiteSpace(artifactsDirFromEnv))
    builder.Configuration["DocAgent:ArtifactsDir"] = artifactsDirFromEnv;

builder.Services.Configure<DocAgentServerOptions>(
    builder.Configuration.GetSection("DocAgent"));

builder.Services.AddSingleton<PathAllowlist>();
builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddDocAgent();  // registers SnapshotStore, BM25SearchIndex, KnowledgeQueryService

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .AddAuditFilter();

var app = builder.Build();

// Startup: load existing index if snapshot exists on disk
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<SnapshotStore>();
    var index = (BM25SearchIndex)scope.ServiceProvider.GetRequiredService<ISearchIndex>();
    var snapshots = await store.ListAsync();
    if (snapshots.Count > 0)
    {
        var latest = snapshots.OrderByDescending(s => s.IngestedAt).First();
        var snapshot = await store.LoadAsync(latest.ContentHash);
        if (snapshot is not null)
            await index.LoadIndexAsync(latest.ContentHash, snapshot);
    }
}

await app.RunAsync();
```

### E2E In-Process Test Pattern

```csharp
// tests/DocAgent.Tests/E2EIntegrationTests.cs
[Trait("Category", "Integration")]
public sealed class E2EIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public E2EIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "E2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() =>
        Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task AllFiveMcpTools_WithRealDI_ReturnNonErrorResponses()
    {
        // Build DI container with real services
        var services = new ServiceCollection();
        services.Configure<DocAgentServerOptions>(o => {
            o.ArtifactsDir = _tempDir;
            o.AllowedPaths = ["**"];
        });
        services.AddDocAgent();

        await using var provider = services.BuildServiceProvider();

        // Pre-populate: build and save a synthetic snapshot
        var store = provider.GetRequiredService<SnapshotStore>();
        var index = (BM25SearchIndex)provider.GetRequiredService<ISearchIndex>();

        var snapshot = BuildSyntheticSnapshot();
        var saved = await store.SaveAsync(snapshot);
        await index.IndexAsync(saved, CancellationToken.None);

        // Resolve DocTools via a scope
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        // Assert all 5 tools return success
        var searchResult = await query.SearchAsync("*");
        searchResult.Success.Should().BeTrue("search_symbols must succeed");
        // ... etc.
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | Notes |
|--------------|------------------|-------|
| Manual `new SnapshotStore(path)` in Program.cs | `AddDocAgent()` extension method | Standard .NET DI extension pattern |
| `InMemorySearchIndex` as fallback | `BM25SearchIndex` as the only `ISearchIndex` in production | `InMemorySearchIndex` stays in `Indexing` project but is test-only |
| `yield break` GetReferencesAsync stub | Graph edge traversal from snapshot | All edges already exist in `SymbolGraphSnapshot.Edges` |

---

## Open Questions

1. **`SymbolNotFoundException` type location**
   - What we know: CONTEXT.md requires throwing this for unknown symbol IDs in `GetReferencesAsync`
   - What's unclear: Should this be a new exception type in `DocAgent.Core`, `DocAgent.Indexing`, or can we reuse `QueryErrorKind.NotFound`?
   - Recommendation: Since `GetReferencesAsync` returns `IAsyncEnumerable` (not `QueryResult<T>`), it cannot return an error result. Define `SymbolNotFoundException : Exception` in `DocAgent.Core` since it's a domain concept. Alternatively, rethink: the interface could be changed to return `QueryResult<IAsyncEnumerable<SymbolEdge>>` but that's a breaking change. Simplest: `SymbolNotFoundException` in Core.

2. **MCP SDK scope lifetime for DocTools**
   - What we know: CONTEXT.md says `IKnowledgeQueryService` is scoped; `DocTools` injects it
   - What's unclear: Does `ModelContextProtocol` v1.0.0's `AddMcpServer()` + `WithToolsFromAssembly()` create a DI scope per tool call? If not, `DocTools` is a singleton and scoped dependencies fail.
   - Recommendation: Check `[McpServerToolType]` resolution in MCP SDK. Based on the existing codebase working (Phase 5 integration tests pass), the framework handles this. If `DocTools` is resolved as transient/scoped by MCP, `KnowledgeQueryService` as scoped will work. If `DocTools` is singleton, `KnowledgeQueryService` must also be singleton. Inspect during 07-01 planning.

3. **Startup index loading — is LoadIndexAsync needed?**
   - What we know: `BM25SearchIndex.IndexAsync` handles the case where a fresh index exists (freshness check via `IsIndexFresh`). `LoadIndexAsync` does the same but throws instead of rebuilding.
   - What's unclear: Can startup just call `IndexAsync` with the latest snapshot (re-validating freshness, skipping rebuild if current)? This avoids a separate startup step.
   - Recommendation: Use `IndexAsync` at startup (it's idempotent due to the freshness check). Avoid adding startup code complexity unless benchmarks show it's too slow.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| Config file | none (xunit auto-discovery) |
| Quick run command | `dotnet test --filter "FullyQualifiedName~E2EIntegrationTests"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| QURY-01 + INDX-01 + INGS-04 | DI container resolves all services without error | Integration (in-process) | `dotnet test --filter "FullyQualifiedName~E2EIntegrationTests"` | ❌ Wave 0 |
| INDX-03 | ArtifactsDir flows to SnapshotStore and BM25SearchIndex | Integration (in-process) | `dotnet test --filter "FullyQualifiedName~E2EIntegrationTests"` | ❌ Wave 0 |
| MCPS-03 | GetReferencesAsync returns real edges (not empty) | Unit + Integration | `dotnet test --filter "FullyQualifiedName~GetReferencesAsync"` | ❌ Wave 0 |
| MCPS-01–05 | All 5 tools return non-error JSON from real DI | E2E in-process | `dotnet test --filter "FullyQualifiedName~E2EIntegrationTests"` | ❌ Wave 0 |
| E2E subprocess | Server starts, responds to all 5 tools via stdio | Subprocess smoke | `dotnet test --filter "Category=Integration"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~GetReferencesAsync OR FullyQualifiedName~E2EIntegrationTests"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/E2EIntegrationTests.cs` — covers DI wiring, ArtifactsDir config, all 5 tools
- [ ] `tests/DocAgent.Tests/GetReferencesAsyncTests.cs` — covers bidirectional edge traversal, SymbolNotFoundException
- [ ] No framework install needed — existing xUnit infrastructure covers all tests

---

## Sources

### Primary (HIGH confidence — direct codebase inspection)
- `src/DocAgent.McpServer/Program.cs` — current state: DI TODO comment, no service registrations
- `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` — missing ArtifactsDir property confirmed
- `src/DocAgent.Indexing/KnowledgeQueryService.cs` — GetReferencesAsync stub confirmed (`yield break`)
- `src/DocAgent.Indexing/BM25SearchIndex.cs` — constructor signature: `BM25SearchIndex(string artifactsDir)` confirmed
- `src/DocAgent.Ingestion/SnapshotStore.cs` — constructor signature: `SnapshotStore(string artifactsDir)` confirmed
- `src/DocAgent.Core/Abstractions.cs` — `IKnowledgeQueryService` interface signature confirmed
- `src/DocAgent.Core/Symbols.cs` — `SymbolEdge`, `SymbolEdgeKind` confirmed (all 7 kinds)
- `tests/DocAgent.Tests/StdoutContaminationTests.cs` — subprocess test pattern confirmed
- `tests/DocAgent.Tests/McpIntegrationTests.cs` — in-process DI test pattern confirmed
- `.planning/v1.0-MILESTONE-AUDIT.md` — gap analysis confirmed: 3 integration gaps
- `.planning/phases/07-runtime-integration-wiring/07-CONTEXT.md` — user decisions confirmed

### Secondary (MEDIUM confidence)
- `Directory.Packages.props` — keyed DI is available (.NET 10 target confirmed)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already referenced, no new dependencies
- Architecture: HIGH — patterns derived from existing codebase, not speculation
- Pitfalls: HIGH — identified from gap between current implementation state and required behavior
- Validation: HIGH — test infrastructure already in place, gaps are clearly identified

**Research date:** 2026-02-27
**Valid until:** 2026-03-27 (stable domain — .NET DI patterns don't change rapidly)
