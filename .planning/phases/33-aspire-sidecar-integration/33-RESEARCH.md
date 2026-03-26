# Phase 33: Aspire Sidecar Integration - Research

**Researched:** 2026-03-26
**Domain:** .NET Aspire AppHost, Aspire.Hosting.JavaScript, ASP.NET Core Health Checks
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Register via `AddNodeApp()` from `Aspire.Hosting.JavaScript` package
- Aspire manages the sidecar lifecycle, shows it in the dashboard
- No startup ordering dependency — MCP server and sidecar start independently (parallel)
- Auto-build at startup: keep existing NodeAvailabilityValidator behavior that runs `npm install && npm run build` if `dist/index.js` is missing
- Degrade gracefully when Node.js is missing — AppHost starts, sidecar resource shows 'unhealthy' in dashboard, MCP server works for C# ingestion only
- Fail immediately with clear TypeScriptIngestionException on sidecar crash — no retry (keep current behavior)
- Keep NodeAvailabilityValidator as IHostedLifecycleService for standalone MCP server mode; Aspire health checks supplement it when running under AppHost
- Aspire health check: `node --version` check to verify Node.js availability (matches existing NodeAvailabilityValidator logic)
- Sidecar appears healthy/unhealthy in Aspire dashboard based on Node.js presence
- MCP server /health endpoint: include sidecar status — return 'degraded' when Node.js is missing
- Centralize sidecar path in `DocAgentServerOptions.SidecarDir`
- Aspire communicates path via `DOCAGENT_SIDECAR_DIR` environment variable from AppHost
- Keep existing fallback chains as standalone-mode fallback
- Both TypeScriptIngestionService and NodeAvailabilityValidator read from the same centralized option

### Claude's Discretion
- Exact AddNodeApp() configuration and arguments
- Whether to refactor TypeScriptIngestionService to communicate with a long-running Aspire process or keep spawn-per-request with Aspire providing validation/path only
- Health check implementation details (custom IHealthCheck vs Aspire built-in)
- How to expose sidecar status in the existing /health endpoint (JSON shape, status codes)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SIDE-04 | Aspire AppHost registers Node.js sidecar via `AddNodeApp()` with startup validation for Node.js availability | AddNodeApp() API confirmed in Aspire.Hosting.JavaScript 13.1.2 (matching AppHost SDK version); custom IHealthCheck pattern via builder.Services.AddHealthChecks().AddCheck() + .WithHealthCheck() confirmed |
</phase_requirements>

---

## Summary

Phase 33 registers the `ts-symbol-extractor` Node.js sidecar as a managed Aspire resource using the `AddNodeApp()` API from `Aspire.Hosting.JavaScript`. This makes the sidecar visible in the Aspire dashboard with live health status, and centralizes the sidecar directory path through a `DOCAGENT_SIDECAR_DIR` environment variable — matching the established `DOCAGENT_ARTIFACTS_DIR` / `DOCAGENT_ALLOWED_PATHS` pattern already in AppHost/Program.cs.

The architecture has two independent layers: the AppHost registers the sidecar as a display resource and injects a custom health check that runs `node --version` to determine availability; the MCP server's existing `NodeAvailabilityValidator` (IHostedLifecycleService) continues to handle the auto-build concern. Neither layer blocks the other — the CONTEXT decision is parallel startup with no `WaitFor()` dependency, so the sidecar resource is informational/monitoring rather than a hard startup gate.

The key implementation decision delegated to Claude is the IPC model: keep spawn-per-request (existing `TypeScriptIngestionService` spawns `node dist/index.js` on each `ingest_typescript` call) versus shifting to a long-running Aspire-managed process. Research supports keeping spawn-per-request for v2.0 per the cold-start isolation rationale; the Aspire registration provides monitoring/path/visibility without changing the IPC contract.

**Primary recommendation:** Add `Aspire.Hosting.JavaScript` 13.1.2 to AppHost, call `AddNodeApp("ts-sidecar", "../ts-symbol-extractor", "dist/index.js")` without `.WaitFor()` or `.WithHttpEndpoint()`, attach a custom `node --version` health check via `builder.Services.AddHealthChecks().AddCheck()` + `.WithHealthCheck()`, and pass the sidecar directory via `.WithEnvironment("DOCAGENT_SIDECAR_DIR", ...)`.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Aspire.Hosting.JavaScript | 13.1.2 | Registers Node.js apps as Aspire resources in AppHost | Official Aspire package; renamed from Aspire.Hosting.NodeJs in 13.0; version 13.1.2 matches the project's existing `Aspire.AppHost.Sdk/13.1.2` |
| Microsoft.Extensions.Diagnostics.HealthChecks | (transitive via ASP.NET Core) | IHealthCheck interface + HealthCheckResult | Built-in; already used via `builder.Services.AddHealthChecks()` in McpServer/Program.cs |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Aspire.Hosting (transitive) | 13.1.2 | Core IDistributedApplicationBuilder | Always present when Aspire.AppHost.Sdk is used |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `AddNodeApp()` | `AddExecutable("node", ...)` | AddExecutable is lower-level; AddNodeApp gives NodeAppResource type, proper dashboard labeling, npm integration. Use AddNodeApp. |
| Custom IHealthCheck class | Inline lambda via `AddCheck(name, () => ...)` | Lambda is simpler for a single `node --version` check; full class adds testability. Both valid — prefer inline lambda for the AppHost side; MCP server side can use a dedicated class. |
| Aspire-managed long-running process | Spawn-per-request (existing) | Long-running avoids cold-start cost but adds complexity and memory leak risk; CONTEXT locks spawn-per-request for v2.0. Keep existing IPC model. |

**Installation (AppHost project only):**
```bash
dotnet add src/DocAgent.AppHost/DocAgent.AppHost.csproj package Aspire.Hosting.JavaScript --version 13.1.2
```
Or add to `Directory.Packages.props`:
```xml
<PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.1.2" />
```
And to `DocAgent.AppHost.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.JavaScript" />
```

---

## Architecture Patterns

### Recommended Project Structure
No new directories needed. Changes are confined to:
```
src/
├── DocAgent.AppHost/
│   ├── Program.cs              # Add AddNodeApp() + health check registration
│   └── DocAgent.AppHost.csproj # Add Aspire.Hosting.JavaScript package reference
├── DocAgent.McpServer/
│   ├── Config/
│   │   └── DocAgentServerOptions.cs  # Ensure SidecarDir reads DOCAGENT_SIDECAR_DIR
│   ├── Program.cs              # Add DOCAGENT_SIDECAR_DIR env var pickup + /health sidecar status
│   └── Validation/
│       └── NodeAvailabilityValidator.cs  # No change needed (kept as-is)
└── Directory.Packages.props    # Add Aspire.Hosting.JavaScript version
```

### Pattern 1: AddNodeApp() Without HTTP Endpoint (Sidecar/Background Worker)

**What:** Register a Node.js process as an Aspire resource for dashboard visibility and health monitoring without requiring an HTTP endpoint.

**When to use:** When the Node.js process communicates via stdin/stdout (NDJSON IPC), not HTTP. The sidecar `dist/index.js` is spawned per-request from C#, not as a persistent HTTP server.

**Example:**
```csharp
// Source: https://aspire.dev/integrations/frameworks/javascript/
// AppHost/Program.cs — sidecar registration without HTTP endpoint

using Aspire.Hosting.JavaScript;   // AddNodeApp extension

var builder = DistributedApplication.CreateBuilder(args);

// Resolve sidecar directory relative to the solution root
var sidecarDir = builder.Configuration["DocAgent:SidecarDir"]
    ?? Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "ts-symbol-extractor"));

// Register custom health check: Node.js availability
builder.Services.AddHealthChecks().AddCheck("node-availability", () =>
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p == null) return HealthCheckResult.Unhealthy("node process could not start");
        p.WaitForExit(3000);
        return p.ExitCode == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("node exited non-zero");
    }
    catch
    {
        return HealthCheckResult.Unhealthy("node not found in PATH");
    }
});

// Register the sidecar as an Aspire resource (dashboard visibility only — no WaitFor)
var sidecar = builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")
    .WithHealthCheck("node-availability");

// MCP server: inject sidecar path as env var (consistent with DOCAGENT_ARTIFACTS_DIR pattern)
var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR",
        builder.Configuration["DocAgent:ArtifactsDir"] ?? "./artifacts")
    .WithEnvironment("DOCAGENT_ALLOWED_PATHS",
        builder.Configuration["DocAgent:AllowlistPaths"] ?? "")
    .WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)
    .WithHttpEndpoint(port: 8089, name: "health")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
```

**Important:** No `.WaitFor(sidecar)` — CONTEXT decision is parallel startup with graceful degradation.

### Pattern 2: Custom IHealthCheck in MCP Server for /health Endpoint

**What:** Extend the existing `/health` ASP.NET Core health check endpoint to report sidecar availability as a "degraded" (not "unhealthy") status — C# ingestion still works, TS ingestion unavailable.

**When to use:** When running in standalone mode (no Aspire) or when Aspire consumers query `/health` to learn about TS capability.

**Example:**
```csharp
// Source: ASP.NET Core health checks documentation
// McpServer — register a named health check with degraded status

builder.Services.AddHealthChecks()
    .AddCheck<NodeAvailabilityHealthCheck>("node-js-sidecar",
        failureStatus: HealthStatus.Degraded,   // not Unhealthy — C# still works
        tags: ["sidecar", "typescript"]);
```

```csharp
// NodeAvailabilityHealthCheck.cs (new small class, reuses NodeAvailabilityValidator logic)
public sealed class NodeAvailabilityHealthCheck : IHealthCheck
{
    private readonly DocAgentServerOptions _options;

    public NodeAvailabilityHealthCheck(IOptions<DocAgentServerOptions> options)
        => _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        var version = await GetNodeVersionAsync(ct);
        var result = NodeAvailabilityValidator.ParseNodeVersion(version);

        if (!result.IsAvailable)
            return HealthCheckResult.Degraded("Node.js not available — TypeScript ingestion disabled");

        if (!result.IsSupported)
            return HealthCheckResult.Degraded(
                $"Node.js {result.ParsedVersion} found but >= 22.0.0 required");

        return HealthCheckResult.Healthy($"Node.js {result.ParsedVersion}");
    }
    // ... GetNodeVersionAsync mirrors NodeAvailabilityValidator
}
```

The `/health` response JSON shape produced by `app.MapHealthChecks("/health")` when using `HealthStatus.Degraded`:
```json
{
  "status": "Degraded",
  "results": {
    "node-js-sidecar": {
      "status": "Degraded",
      "description": "Node.js not available — TypeScript ingestion disabled"
    }
  }
}
```

Consumers see `"status": "Degraded"` (not `"Unhealthy"`) when C# ingestion works but TS ingestion does not.

### Pattern 3: DOCAGENT_SIDECAR_DIR Env Var Pickup in McpServer

**What:** McpServer/Program.cs already picks up `DOCAGENT_ARTIFACTS_DIR` into configuration. Same pattern for `DOCAGENT_SIDECAR_DIR`.

**Example:**
```csharp
// McpServer/Program.cs — add after the existing DOCAGENT_ARTIFACTS_DIR block
var sidecarDirFromEnv = Environment.GetEnvironmentVariable("DOCAGENT_SIDECAR_DIR");
if (sidecarDirFromEnv is not null)
    builder.Configuration["DocAgent:SidecarDir"] = sidecarDirFromEnv;
```

`DocAgentServerOptions.SidecarDir` already exists and both `NodeAvailabilityValidator` and `TypeScriptIngestionService` already read from it. This single addition closes the path-centralization gap.

### Anti-Patterns to Avoid

- **Adding `.WaitFor(sidecar)` on the MCP server:** CONTEXT explicitly prohibits startup ordering dependency. This would block the MCP server from starting if Node.js is absent.
- **Registering Aspire.Hosting.JavaScript in DocAgent.McpServer.csproj:** The package belongs only in the AppHost project. McpServer must remain usable standalone (without Aspire).
- **Replacing NodeAvailabilityValidator with Aspire health check:** NodeAvailabilityValidator handles the auto-build concern (`npm install && npm run build`) which Aspire health checks cannot express. Keep both: Aspire health check for dashboard status, NodeAvailabilityValidator for build lifecycle.
- **Using `Aspire.Hosting.NodeJs` (old package name):** This was renamed to `Aspire.Hosting.JavaScript` in Aspire 13.0. The old package name is obsolete.
- **Using old `AddNpmApp()` or old `AddNodeApp(name, scriptPath, workingDir, args[])` signature:** Breaking change in Aspire 13.0 — new signature is `AddNodeApp(name, appDirectory, scriptPath)` with no `args[]` parameter. Use `.WithArgs()` if arguments are needed.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Aspire dashboard resource registration | Custom resource class | `AddNodeApp()` from Aspire.Hosting.JavaScript | Built-in; provides NodeAppResource, proper lifecycle hooks, dashboard labels |
| Health check result aggregation in /health endpoint | Manual JSON serialization | `app.MapHealthChecks("/health")` (already in use) | ASP.NET Core's built-in aggregation handles all registered checks |
| Aspire resource health association | Custom Aspire extension | `builder.Services.AddHealthChecks().AddCheck() + .WithHealthCheck()` | Standard Aspire pattern; integrates with dashboard without custom plumbing |

---

## Common Pitfalls

### Pitfall 1: Wrong AddNodeApp Parameter Order (Breaking Change in Aspire 13.0)
**What goes wrong:** Using old parameter order `AddNodeApp(name, scriptPath, workingDir)` causes a compile error or runtime mis-routing.
**Why it happens:** Aspire 13.0 introduced a breaking change — parameter order changed from `(name, scriptPath, workingDir?)` to `(name, appDirectory, scriptPath)`.
**How to avoid:** Use `builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")` — directory first, then script file.
**Warning signs:** Compile error referencing `NodeAppHostingExtension.AddNodeApp` with wrong parameter types; or Node.js attempting to run the directory path as a script.

### Pitfall 2: Sidecar Directory Resolution in AppHost
**What goes wrong:** Relative path like `"../ts-symbol-extractor"` resolves differently depending on working directory when running `dotnet run --project src/DocAgent.AppHost`.
**Why it happens:** The working directory when Aspire starts may not be the solution root; `builder.AppHostDirectory` is the AppHost project directory.
**How to avoid:** Use `Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "ts-symbol-extractor"))` to compute an absolute path at AppHost startup. Then pass this absolute path both to `AddNodeApp()` and to `DOCAGENT_SIDECAR_DIR`.
**Warning signs:** `DirectoryNotFoundException` at Aspire startup, or NodeAvailabilityValidator warning "sidecar directory not found."

### Pitfall 3: Health Check FailureStatus Must Be Degraded, Not Unhealthy
**What goes wrong:** Using `HealthStatus.Unhealthy` for the Node.js check in McpServer causes the `/health` endpoint to return HTTP 503, which breaks Aspire's own `/health` probe (`WithHttpHealthCheck("/health")`).
**Why it happens:** Aspire polls the McpServer `/health` endpoint; if it returns 503, Aspire marks the MCP server itself as unhealthy — even though C# ingestion works fine.
**How to avoid:** Register the Node.js health check with `failureStatus: HealthStatus.Degraded`. Degraded produces HTTP 200 (not 503), so Aspire's poll succeeds and the MCP server stays "running" in the dashboard. Only set `Unhealthy` for checks that indicate the entire service is down.
**Warning signs:** MCP server showing as unhealthy in Aspire dashboard when only Node.js is missing; C# tools returning errors even though Lucene/Roslyn work fine.

### Pitfall 4: Package Added to Wrong Project
**What goes wrong:** `Aspire.Hosting.JavaScript` referenced in `DocAgent.McpServer.csproj` instead of `DocAgent.AppHost.csproj`.
**Why it happens:** Easy mistake when adding via `dotnet add package` from the wrong directory.
**How to avoid:** The `AddNodeApp()` extension method is an AppHost concern only. McpServer must stay standalone-runnable without Aspire hosting packages.
**Warning signs:** McpServer binary size increases; `IDistributedApplicationBuilder` referenced in McpServer build output.

### Pitfall 5: Missing `using Aspire.Hosting.JavaScript;` Namespace
**What goes wrong:** `AddNodeApp()` not found as an extension method despite package being referenced.
**Why it happens:** `AddNodeApp` is an extension method in the `Aspire.Hosting.JavaScript` namespace (not `Aspire.Hosting`).
**How to avoid:** Add `using Aspire.Hosting.JavaScript;` to AppHost/Program.cs (or rely on global usings if configured).
**Warning signs:** CS1061 "IDistributedApplicationBuilder does not contain a definition for 'AddNodeApp'."

---

## Code Examples

Verified patterns from official sources:

### AddNodeApp() — Full Registration with Health Check
```csharp
// Source: https://aspire.dev/integrations/frameworks/javascript/ + https://aspire.dev/fundamentals/health-checks.md
// AppHost/Program.cs

using Aspire.Hosting.JavaScript;

var builder = DistributedApplication.CreateBuilder(args);

// Compute absolute sidecar path from AppHost directory
var sidecarDir = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "ts-symbol-extractor"));

// Register Node.js availability health check
builder.Services.AddHealthChecks().AddCheck("node-js-available", () =>
{
    try
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "node", Arguments = "--version",
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        });
        if (p == null) return HealthCheckResult.Unhealthy("node could not start");
        p.WaitForExit(3_000);
        return p.ExitCode == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("node failed");
    }
    catch (Exception ex)
    {
        return HealthCheckResult.Unhealthy($"node not found: {ex.Message}");
    }
});

// Register sidecar resource — no WaitFor (parallel start, graceful degradation)
var sidecar = builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")
    .WithHealthCheck("node-js-available");

// MCP server with sidecar path injection
builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR",
        builder.Configuration["DocAgent:ArtifactsDir"] ?? "./artifacts")
    .WithEnvironment("DOCAGENT_ALLOWED_PATHS",
        builder.Configuration["DocAgent:AllowlistPaths"] ?? "")
    .WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)
    .WithHttpEndpoint(port: 8089, name: "health")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
```

### AddCheck() + WithHealthCheck() — AppHost Pattern
```csharp
// Source: https://aspire.dev/fundamentals/health-checks.md
builder.Services.AddHealthChecks().AddCheck("mycheck", () =>
    DateTime.Now > startAfter
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy());

var resource = builder.AddNodeApp(...)
    .WithHealthCheck("mycheck");
```

### HealthStatus.Degraded for Optional Dependency in McpServer
```csharp
// Source: ASP.NET Core health checks documentation
// Register with Degraded (not Unhealthy) so /health returns HTTP 200 when Node is absent
builder.Services.AddHealthChecks()
    .AddCheck<NodeAvailabilityHealthCheck>(
        "node-js-sidecar",
        failureStatus: HealthStatus.Degraded);
```

### DOCAGENT_SIDECAR_DIR Pickup (matches existing DOCAGENT_ARTIFACTS_DIR pattern)
```csharp
// Source: existing McpServer/Program.cs pattern
var sidecarDirFromEnv = Environment.GetEnvironmentVariable("DOCAGENT_SIDECAR_DIR");
if (sidecarDirFromEnv is not null)
    builder.Configuration["DocAgent:SidecarDir"] = sidecarDirFromEnv;
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `Aspire.Hosting.NodeJs` package | `Aspire.Hosting.JavaScript` package | Aspire 13.0 (Dec 2025) | Must use new package name |
| `AddNpmApp(name, workingDir, scriptName)` | `AddJavaScriptApp(name, appDirectory)` | Aspire 13.0 | AddNpmApp removed |
| `AddNodeApp(name, scriptPath, workingDir?, args[]?)` | `AddNodeApp(name, appDirectory, scriptPath)` | Aspire 13.0 | Parameter order reversed; args[] removed, use `.WithArgs()` |

**Deprecated/outdated:**
- `Aspire.Hosting.NodeJs`: Renamed to `Aspire.Hosting.JavaScript` in 13.0. Old NuGet package still exists but targets Aspire <=9.x only.
- `AddNpmApp()`: Removed in Aspire 13.0; replaced by `AddJavaScriptApp()`.

---

## Open Questions

1. **`builder.AppHostDirectory` availability**
   - What we know: Aspire's `IDistributedApplicationBuilder` exposes `AppHostDirectory` for path resolution from AppHost project root.
   - What's unclear: Whether the property is named `AppHostDirectory` or `ApplicationName`/`Environment.ContentRootPath` in Aspire 13.1.x.
   - Recommendation: Verify with `builder.GetType().GetProperties()` or check Aspire 13.1 source. Fallback: use `AppContext.BaseDirectory` (resolves to AppHost bin dir at runtime) or `Directory.GetCurrentDirectory()` (working dir when Aspire starts).

2. **Health check in AppHost vs. health check in McpServer — duplication**
   - What we know: CONTEXT calls for a health check in AppHost (dashboard) AND in McpServer /health endpoint (consumer API). Both run `node --version`.
   - What's unclear: Whether running `node --version` in AppHost blocks the Aspire startup thread.
   - Recommendation: In AppHost use async health check if IHealthCheck async interface is available; or run `Process.Start` with a short timeout (3 seconds) so Aspire startup isn't blocked.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| Config file | none (standard xUnit discovery) |
| Quick run command | `dotnet test --filter "FullyQualifiedName~NodeAvailability"` |
| Full suite command | `dotnet test src/DocAgentFramework.sln` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SIDE-04 | AppHost Program.cs compiles with AddNodeApp registration | build | `dotnet build src/DocAgent.AppHost` | ❌ Wave 0 (code change) |
| SIDE-04 | NodeAvailabilityHealthCheck returns Degraded when node absent | unit | `dotnet test --filter "FullyQualifiedName~NodeAvailabilityHealthCheck"` | ❌ Wave 0 |
| SIDE-04 | /health endpoint returns Degraded (HTTP 200) when Node missing | unit | `dotnet test --filter "FullyQualifiedName~HealthEndpoint"` | ❌ Wave 0 |
| SIDE-04 | DOCAGENT_SIDECAR_DIR env var sets DocAgentServerOptions.SidecarDir | unit | `dotnet test --filter "FullyQualifiedName~SidecarDir"` | ❌ Wave 0 |
| SIDE-04 | Existing NodeAvailabilityValidatorTests still pass (no regression) | unit | `dotnet test --filter "FullyQualifiedName~NodeAvailabilityValidatorTests"` | ✅ Exists |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~NodeAvailability"`
- **Per wave merge:** `dotnet test src/DocAgentFramework.sln`
- **Phase gate:** Full suite green (649+ tests) before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs` — covers SIDE-04 health check behavior (Degraded when node absent, Healthy when present, version parse)
- [ ] `tests/DocAgent.Tests/HealthEndpointTests.cs` — covers SIDE-04 /health degraded response (or add to existing integration test if one exists)
- [ ] `tests/DocAgent.Tests/SidecarDirEnvVarTests.cs` — covers DOCAGENT_SIDECAR_DIR → DocAgentServerOptions.SidecarDir binding

---

## Sources

### Primary (HIGH confidence)
- `https://aspire.dev/integrations/frameworks/javascript/` — AddNodeApp() signature, parameter order, Aspire.Hosting.JavaScript package
- `https://aspire.dev/fundamentals/health-checks.md` — AddCheck() + WithHealthCheck() pattern, dashboard integration
- `https://www.nuget.org/packages/Aspire.Hosting.JavaScript` — version 13.1.2 confirmed as of 2026-02-26 (matches AppHost SDK)
- Existing codebase: `src/DocAgent.AppHost/Program.cs`, `src/DocAgent.AppHost/DocAgent.AppHost.csproj`, `src/DocAgent.McpServer/Validation/NodeAvailabilityValidator.cs`, `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs`

### Secondary (MEDIUM confidence)
- `https://github.com/dotnet/docs-aspire/issues/5444` — Breaking change documentation: old vs new AddNodeApp signature confirmed
- `https://devblogs.microsoft.com/aspire/aspire-for-javascript-developers/` (Jan 2026) — AddNodeApp() usage examples, JavaScript ecosystem support
- `https://aspire.dev/whats-new/aspire-13/` — Aspire 13.0 breaking changes summary

### Tertiary (LOW confidence)
- Stack Overflow thread on health check patterns — corroborates HealthStatus.Degraded usage but single source

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Aspire.Hosting.JavaScript package/version confirmed via NuGet; AddNodeApp() signature confirmed via official docs and GitHub issue
- Architecture: HIGH — AppHost pattern matches existing DOCAGENT_ARTIFACTS_DIR pattern in codebase; health check pattern confirmed via Aspire docs
- Pitfalls: HIGH — Breaking change in parameter order confirmed via GitHub issue #5444; Degraded vs Unhealthy distinction verified via ASP.NET Core health check docs

**Research date:** 2026-03-26
**Valid until:** 2026-04-26 (stable Aspire 13.1.x; next minor could add new patterns but won't break 13.1.2 usage)
