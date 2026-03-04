# Stack Research

**Domain:** .NET 10 MCP server — v1.5 Robustness additions only
**Researched:** 2026-03-04
**Confidence:** HIGH (NuGet package versions verified directly; .NET BCL APIs verified via official docs)

---

## New Stack Additions for v1.5

This is a **delta document** — only what is NEW or CHANGED versus the existing validated stack.
Existing stack (Roslyn 4.12, Lucene.Net 4.8-beta, MessagePack 3.1.4, ModelContextProtocol 1.0.0, Aspire, OTel, BenchmarkDotNet) is unchanged unless explicitly listed below.

---

## Rate Limiting

### Recommended: `System.Threading.RateLimiting` (in-box, no new package)

DocAgentFramework uses **stdio MCP transport** — there is no ASP.NET Core pipeline, so `Microsoft.AspNetCore.RateLimiting` middleware is the wrong abstraction. Use the lower-level `System.Threading.RateLimiting` namespace directly, which ships in the .NET 10 runtime with no additional NuGet package required.

| API | Version | Purpose | Why |
|-----|---------|---------|-----|
| `System.Threading.RateLimiting.TokenBucketRateLimiter` | .NET 10 BCL (built-in) | Per-session tool call rate limiting | Handles bursty LLM tool calls; allows short burst then enforces average rate |
| `System.Threading.RateLimiting.ConcurrencyLimiter` | .NET 10 BCL (built-in) | Bound concurrent ingestion operations | Prevents overload from simultaneous `ingest_solution` calls |

**Integration point:** Wrap MCP tool dispatch in a `RateLimiter.AcquireAsync()` guard inside `DocAgentServerOptions`-configured middleware. For stdio (single-agent connection), a single global `TokenBucketRateLimiter` is sufficient — no per-tenant partitioning needed.

**Algorithm choice — token bucket over fixed window:** Fixed window causes request clustering at window boundaries. Token bucket smooths traffic and is idiomatic for tool-call scenarios where an LLM bursts calls then pauses.

**No new package needed.** `System.Threading.RateLimiting` is part of .NET 8+ runtime. For .NET 10 the standalone NuGet `System.Threading.RateLimiting` exists at `10.0.3` but is only needed when targeting older frameworks. Since the project targets `net10.0`, the BCL version is used automatically.

---

## Roslyn Upgrade: 4.12.0 → 4.14.0

### Recommendation: Upgrade to 4.14.0, NOT 5.0.0

| Package | Current | Target | Rationale |
|---------|---------|--------|-----------|
| `Microsoft.CodeAnalysis.CSharp` | 4.12.0 | **4.14.0** | Released May 15, 2025; already required by BenchmarkDotNet 0.15.8 (see `Microsoft.CodeAnalysis.Common` VersionOverride in `Directory.Packages.props`) |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | 4.12.0 | **4.14.0** | Matches CSharp package; MSBuildWorkspace stays aligned |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 4.12.0 | **4.14.0** | Must move in lockstep with Workspaces |
| `Microsoft.CodeAnalysis.Common` | 4.14.0 (VersionOverride) | **4.14.0** (promote to primary) | Already pinned at this version to resolve NU1107; promote to official version, remove VersionOverride |

**Why 4.14.0 and not 5.0.0:**
- `Microsoft.CodeAnalysis.CSharp 5.0.0` is available on NuGet but targets VS 2022 17.14+ / .NET SDK that ships with C# 14 preview features. MSBuildWorkspace compatibility with 5.0.0 has no confirmed test record in this project.
- 4.14.0 is already partially in the dependency graph (BenchmarkDotNet forces `Microsoft.CodeAnalysis.Common` 4.14.0 via VersionOverride). Promoting all Roslyn packages to 4.14.0 **resolves the existing NU1107 conflict cleanly** — no more VersionOverride needed.
- Risk-adjusted: 4.14 is a stable minor bump with known changelog; 4.14 → 5.0 is a major version crossing with unknown MSBuildWorkspace surface changes.

**Migration effort:** Low. Bump three package versions in `Directory.Packages.props`, remove the `Microsoft.CodeAnalysis.Common` VersionOverride in `DocAgent.Tests.csproj`. No API changes expected at 4.12→4.14 for the symbol/workspace APIs in use.

**Analyzer testing packages** (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2`, `Microsoft.CodeAnalysis.Testing.Verifiers.XUnit 1.1.2`) require verification — these pin their own Roslyn transitive dep. If they conflict at 4.14, the VersionOverride pattern stays on those packages only.

---

## Package Auditing

### Recommended: Built-in `dotnet list package` + `dotnet-outdated-tool`

**No new runtime package.** Auditing is a developer workflow, not a runtime dependency.

| Tool | Version | Purpose | How to Use |
|------|---------|---------|-----------|
| `dotnet list package --vulnerable` | .NET SDK built-in (SDK 5.0.200+) | Scan direct + transitive deps against GitHub Advisory Database | `dotnet list package --vulnerable --include-transitive` at solution level |
| `dotnet list package --deprecated` | .NET SDK built-in | Flag deprecated packages | `dotnet list package --deprecated` |
| `dotnet-outdated-tool` | 4.7.0 (global tool) | Report all outdated NuGet packages with latest stable/preview | `dotnet tool install -g dotnet-outdated-tool && dotnet outdated` |

**NuGetAudit MSBuild property** (NuGet 6.8+, available in .NET 9/10 SDK): Add `<NuGetAudit>true</NuGetAudit>` to `Directory.Build.props` to surface vulnerability warnings at `dotnet restore` time. This is a zero-cost, zero-package-dependency addition that runs in CI automatically.

---

## Pagination

### Recommendation: Custom cursor-based pagination, no new package

MCP tool results are plain C# objects serialized to JSON. Pagination for `search_symbols` and coverage results does not require a library — implement as a lightweight cursor pattern on the domain layer.

**Pattern:**

```csharp
// Request: add to existing tool input
public record SearchRequest(string Query, int PageSize = 50, string? Cursor = null);

// Response: extend existing tool output
public record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor, int TotalCount);
```

Cursor = opaque base64-encoded offset (e.g., `Convert.ToBase64String(BitConverter.GetBytes(offset))`). This keeps cursors stable across re-queries without server state.

**Why not keyset/offset pagination libraries:** The search index is Lucene.Net, which already supports `ScoreDoc` as a search-after token. Use `IndexSearcher.SearchAfter(ScoreDoc after, Query, int n)` — this is the idiomatic Lucene pagination API already in the dependency graph. No new package.

**PageSize defaults:** 50 items default, 200 max. Enforce max server-side to bound memory.

---

## find_implementations Tool

### Recommendation: Graph-based edge query over existing `SymbolGraphSnapshot`

`Microsoft.CodeAnalysis.Workspaces` (already referenced) provides `SymbolFinder.FindImplementationsAsync` for interface/abstract member implementation lookup, but it requires a live Roslyn `Solution` object and keeping `MSBuildWorkspace` warm in memory.

For v1.5, the better approach is querying `EdgeKind.Implements` edges already present in `SymbolEdge` within the snapshot. This is an O(n) scan over edges with no new dependency.

Live Roslyn `SymbolFinder` approach is deferred — higher memory cost, not needed for v1.5 scope.

---

## doc_coverage_metrics Tool

### Recommendation: No new packages — derive from existing `SymbolGraphSnapshot`

Doc coverage is computed over `SymbolNode.DocComment` presence on public symbols. The existing `DocCoverageAnalyzer` Roslyn analyzer already tracks this at build time. For the MCP tool, aggregate at query time from the snapshot — no new library needed.

---

## Startup Validation

### Recommendation: `Microsoft.Extensions.Options` validation (already in hosting stack)

Use `IOptions<T>` with `ValidateOnStart()` and `ValidateDataAnnotations()` — both ship in `Microsoft.Extensions.Hosting` (already at `10.0.0-preview.2`). No new package.

```csharp
services.AddOptions<DocAgentServerOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Add `[Required]`, `[Range]`, and custom `IValidateOptions<T>` implementations to `DocAgentServerOptions` for path allowlist, rate limit config, and pagination defaults.

---

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| `Microsoft.CodeAnalysis.CSharp` 4.14.0 | `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.14.0 | Must be same version — all three Roslyn packages move together |
| `Microsoft.CodeAnalysis.CSharp` 4.14.0 | `BenchmarkDotNet` 0.15.8 | BDN forces `Common` 4.14.0 — aligning main packages eliminates the VersionOverride hack |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` 1.1.2 | `Microsoft.CodeAnalysis.CSharp` 4.14.0 | Verify after bump — these testing packages pin Roslyn transitively; VersionOverride may still be needed on test project only |
| `System.Threading.RateLimiting` | `net10.0` | BCL-native on .NET 10; no NuGet package reference needed |

---

## What NOT to Add

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | Major version; MSBuildWorkspace compatibility unverified; no feature in v1.5 requires it | Stay at 4.14.0 |
| `Microsoft.AspNetCore.RateLimiting` | Requires ASP.NET Core pipeline; this is a stdio MCP server | `System.Threading.RateLimiting` BCL types directly |
| `AspNetCoreRateLimit` (stefanprodan) | Web-only; adds ASP.NET dep chain | BCL `TokenBucketRateLimiter` |
| External pagination library | No value for simple cursor-over-offset pattern | Lucene `SearchAfter` + custom cursor |
| Third-party package audit services | SDK built-in `dotnet list package --vulnerable` covers the need | `dotnet list package --vulnerable --include-transitive` + `NuGetAudit` MSBuild property |

---

## Directory.Packages.props Changes Summary

Three lines change, one line is removed:

```xml
<!-- CHANGE: Roslyn bump — replace all three 4.12.0 entries -->
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.14.0" />
<PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.14.0" />

<!-- REMOVE: VersionOverride workaround no longer needed after aligning to 4.14.0 -->
<!-- <PackageVersion Include="Microsoft.CodeAnalysis.Common" Version="4.14.0" /> -->
```

Also remove the `VersionOverride="4.14.0"` attribute on `Microsoft.CodeAnalysis.Common` in `DocAgent.Tests.csproj`.

**No new `<PackageVersion>` entries required for any other v1.5 feature.**

---

## Sources

- [NuGet: Microsoft.CodeAnalysis.CSharp 4.14.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/4.14.0) — release date May 15, 2025 confirmed (HIGH confidence)
- [NuGet: Microsoft.CodeAnalysis.CSharp 5.0.0](https://www.nuget.org/packages/microsoft.codeanalysis.csharp/) — major version exists; not recommended for v1.5 (HIGH confidence)
- [Microsoft Learn: Rate limiting middleware in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) — verified ASP.NET approach; identified inapplicability to stdio transport (HIGH confidence)
- [.NET Blog: Announcing Rate Limiting for .NET](https://devblogs.microsoft.com/dotnet/announcing-rate-limiting-for-dotnet/) — BCL `System.Threading.RateLimiting` design rationale (HIGH confidence)
- [Microsoft Learn: Auditing NuGet package dependencies](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) — `dotnet list package --vulnerable` and `NuGetAudit` property (HIGH confidence)
- [NuGet: dotnet-outdated-tool 4.7.0](https://www.nuget.org/packages/dotnet-outdated-tool) — global tool version confirmed (HIGH confidence)
- [fast.io: MCP Server Rate Limiting](https://fast.io/resources/mcp-server-rate-limiting/) — stdio token bucket pattern guidance (MEDIUM confidence — blog source)

---

*Stack research for: DocAgentFramework v1.5 Robustness*
*Researched: 2026-03-04*
