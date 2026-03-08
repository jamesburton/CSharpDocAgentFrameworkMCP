# Phase 25 Research: Server Infrastructure

**Phase:** 25 - Server Infrastructure
**Confidence:** HIGH
**Researched:** 2026-03-08

## Summary

Two features needed: startup configuration validation (OPS-02) and rate limiting (OPS-03). Both build on existing patterns — `AuditFilter` for the MCP filter pipeline, `DocAgentServerOptions` for configuration, and `PathAllowlist` for the DI/Options pattern.

## Current Architecture

### Configuration
- `DocAgentServerOptions` (`src/DocAgent.McpServer/Config/DocAgentServerOptions.cs`) — strongly-typed options bound from `appsettings.json` section "DocAgent"
- Properties: `AllowedPaths[]`, `DeniedPaths[]`, `VerboseErrors`, `ArtifactsDir`, `IngestionTimeoutSeconds`, `Audit`
- Bound in `Program.cs:29-30` via `builder.Services.Configure<DocAgentServerOptions>(...)`

### MCP Filter Pipeline
- `AuditFilter` (`src/DocAgent.McpServer/Filters/AuditFilter.cs`) — wraps every tool call via `AddCallToolFilter`
- Pattern: `builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, ct) => { ... }));`
- Returns structured `CallToolResult` with `IsError = true` on exceptions — no unhandled exceptions escape
- Chained in `Program.cs:72`: `.AddAuditFilter()`

### Security
- `PathAllowlist` (`src/DocAgent.McpServer/Security/PathAllowlist.cs`) — DI singleton, takes `IOptions<DocAgentServerOptions>`
- Merges config + env var (`DOCAGENT_ALLOWED_PATHS`)
- Default when unconfigured: allows cwd only

### Server Startup
- `Program.cs` — linear startup: build → health checks → load existing snapshot → `app.RunAsync()`
- **No startup validation currently exists** — if AllowedPaths is empty, PathAllowlist silently defaults to cwd
- **No rate limiting currently exists**
- stdout is reserved for MCP JSON-RPC framing — all diagnostics must go to stderr (already configured via `LogToStandardErrorThreshold = LogLevel.Trace`)

### Existing Test Patterns
- `PathAllowlistTests` — constructs `DocAgentServerOptions` + `Options.Create()` without any host or DI container
- `AuditLoggerTests` — tests audit logger in isolation
- `McpIntegrationTests` — full integration tests with MCP server

## OPS-02: Startup Configuration Validation

### Approach

Use `IHostedLifecycleService.StartingAsync` which runs before other hosted services' `StartAsync`, ensuring validation happens before the server accepts any tool calls.

**Key design decisions:**
1. Extract a pure `static Validate(DocAgentServerOptions)` method for unit testability via `ServiceCollection` alone (no host needed)
2. Print diagnostic summary to stderr (via ILogger which is already configured to stderr)
3. Exit non-zero on fatal validation errors
4. Validation checks:
   - AllowedPaths non-empty OR DOCAGENT_ALLOWED_PATHS env var set (warn if using cwd default)
   - ArtifactsDir is set and writable (try create + write test file + delete)
   - ArtifactsDir is not null (fatal)

**Files to create/modify:**
| File | Action |
|------|--------|
| `src/DocAgent.McpServer/Validation/StartupValidator.cs` | NEW — `IHostedLifecycleService` + static `Validate()` |
| `src/DocAgent.McpServer/Program.cs` | Register `StartupValidator` as hosted service |
| `tests/DocAgent.Tests/StartupValidatorTests.cs` | NEW — unit tests for Validate() |

### Testability
The pure `Validate()` method accepts `DocAgentServerOptions` and returns a validation result (errors/warnings list). Unit tests construct options directly without needing a host. The `IHostedLifecycleService` wrapper is thin — just calls Validate() and acts on the result.

## OPS-03: Rate Limiting

### Approach

Use `System.Threading.RateLimiting.TokenBucketRateLimiter` from the BCL. Create a rate limit filter following the same pattern as `AuditFilter`.

**Key design decisions:**
1. Two separate `TokenBucketRateLimiter` instances — one for query tools, one for ingestion tools
2. Tool categorization: `ingest_project` and `ingest_solution` are ingestion; everything else is query
3. Filter returns structured `CallToolResult` with `IsError = true` and JSON body `{error: "rate_limited", message: ..., retryAfterMs: ...}` — no exceptions thrown
4. Rate limiter is a DI singleton — shared across all tool calls
5. Configuration via `RateLimitOptions` nested in `DocAgentServerOptions`

**Configuration model:**
```csharp
public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int QueryTokenLimit { get; set; } = 100;
    public int QueryTokensPerPeriod { get; set; } = 100;
    public int QueryReplenishmentPeriodSeconds { get; set; } = 60;
    public int IngestionTokenLimit { get; set; } = 10;
    public int IngestionTokensPerPeriod { get; set; } = 10;
    public int IngestionReplenishmentPeriodSeconds { get; set; } = 60;
}
```

**Filter pipeline ordering:**
1. Rate limit filter (first — reject early before doing work)
2. Audit filter (second — logs all calls including rate-limited ones)

**Files to create/modify:**
| File | Action |
|------|--------|
| `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` | Add `RateLimitOptions RateLimit` property |
| `src/DocAgent.McpServer/Filters/RateLimitFilter.cs` | NEW — `AddRateLimitFilter()` extension |
| `src/DocAgent.McpServer/RateLimiting/ToolRateLimiter.cs` | NEW — DI singleton wrapping two TokenBucketRateLimiters |
| `src/DocAgent.McpServer/Program.cs` | Register rate limiter + add filter before audit filter |
| `tests/DocAgent.Tests/RateLimitTests.cs` | NEW — unit tests |

### NuGet Dependency
`System.Threading.RateLimiting` — check if already in .NET 10 shared framework. If not, add to `Directory.Packages.props`. It's been in the BCL since .NET 7, should be available without additional NuGet reference.

### Structured Error Response
```json
{
  "error": "rate_limited",
  "message": "Too many requests. Please retry after 1200ms.",
  "retryAfterMs": 1200
}
```

Returned as `CallToolResult { Content = [TextContentBlock], IsError = true }` — same pattern as AuditFilter's error handling.

## Determinism / Ordering Impact

None. These are infrastructure features that don't touch the symbol graph, snapshots, or serialization.

## Open Questions

1. Whether `System.Threading.RateLimiting` needs explicit NuGet reference on .NET 10 (likely in shared framework already)
2. Filter ordering — should rate limit filter run before or after audit? Before is better (reject early), but then rate-limited calls won't appear in audit. Could audit both — run audit filter outermost, rate limit inside.

## Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Standard Stack | HIGH | `System.Threading.RateLimiting` is stable BCL; MCP filter pipeline proven |
| Architecture | HIGH | All patterns derived from existing codebase (AuditFilter, DocAgentServerOptions, PathAllowlist) |
| Pitfalls | HIGH | stdout guard already handled; filter ordering identified; disposal considered |
| Testability | HIGH | Pure validation method pattern proven by PathAllowlistTests |
