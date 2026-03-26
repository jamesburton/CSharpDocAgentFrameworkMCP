---
phase: 33-aspire-sidecar-integration
plan: "01"
subsystem: aspire-sidecar
tags: [aspire, health-checks, sidecar, nodejs, configuration]
dependency_graph:
  requires: []
  provides:
    - NodeAvailabilityHealthCheck IHealthCheck implementation
    - DOCAGENT_SIDECAR_DIR env var wiring from AppHost to McpServer
    - Aspire ts-sidecar resource via AddNodeApp()
  affects:
    - src/DocAgent.AppHost/Program.cs
    - src/DocAgent.McpServer/Program.cs
    - src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs
    - tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs
tech_stack:
  added:
    - Aspire.Hosting.JavaScript v13.1.2 (sidecar resource registration)
    - Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck (NodeAvailabilityHealthCheck)
  patterns:
    - Injectable Func<CancellationToken, Task<string?>> for testable process invocation
    - Degraded (not Unhealthy) health status to keep /health at HTTP 200 without Node.js
    - PathExpander.Expand() pattern for env var pickup into configuration
key_files:
  created:
    - src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs
    - tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs
  modified:
    - src/DocAgent.McpServer/Validation/NodeAvailabilityValidator.cs (ParseNodeVersion internal → public)
    - src/DocAgent.McpServer/Program.cs (health check registration + DOCAGENT_SIDECAR_DIR pickup)
    - src/DocAgent.AppHost/Program.cs (AddNodeApp + DOCAGENT_SIDECAR_DIR injection)
    - src/DocAgent.AppHost/DocAgent.AppHost.csproj (Aspire.Hosting.JavaScript package ref)
    - Directory.Packages.props (Aspire.Hosting.JavaScript v13.1.2 version entry)
decisions:
  - "NodeAvailabilityHealthCheck returns Degraded not Unhealthy — keeps /health HTTP 200 for Aspire probes"
  - "Injectable Func<CancellationToken, Task<string?>> versionProvider makes health check unit-testable without Node.js on CI"
  - "ParseNodeVersion promoted to public static so health check can reuse it across assembly boundary"
  - "AppHost-level health check removed (no AddHealthChecks in Aspire AppHost SDK) — only McpServer /health matters"
  - "No .WaitFor(sidecar) on McpServer — parallel startup with graceful degradation as specified"
  - "SidecarDirEnvVar test uses Path.GetTempPath() prefix for cross-platform compatibility"
metrics:
  duration_seconds: 2442
  completed_date: "2026-03-26"
  tasks_completed: 2
  tasks_total: 2
  files_created: 2
  files_modified: 5
  tests_added: 5
  test_total_after: 654
---

# Phase 33 Plan 01: Aspire Sidecar Integration Summary

NodeAvailabilityHealthCheck (Degraded on absent Node.js) plus Aspire AddNodeApp() registration and DOCAGENT_SIDECAR_DIR env var wiring from AppHost through McpServer into DocAgentServerOptions.SidecarDir.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | NodeAvailabilityHealthCheck class and unit tests | 7752114 | NodeAvailabilityHealthCheck.cs, NodeAvailabilityHealthCheckTests.cs |
| 2 | AppHost sidecar registration and DOCAGENT_SIDECAR_DIR env var wiring | 04110c5 | AppHost/Program.cs, McpServer/Program.cs, Directory.Packages.props |

## What Was Built

### NodeAvailabilityHealthCheck (Task 1)

New `IHealthCheck` implementation in `DocAgent.McpServer.Validation`:

- Constructor takes `IOptions<DocAgentServerOptions>` (reads `NodeExecutable`) plus optional `Func<CancellationToken, Task<string?>> versionProvider` for testing
- `CheckHealthAsync` runs `node --version` via `Process.Start` with 3-second timeout, passes output to `NodeAvailabilityValidator.ParseNodeVersion`
- Returns `HealthCheckResult.Degraded(...)` when Node is absent or unsupported (NOT Unhealthy)
- Returns `HealthCheckResult.Healthy("Node.js {version}")` when Node is present and supported
- All exceptions caught and returned as Degraded

Registered in McpServer `Program.cs`:
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<NodeAvailabilityHealthCheck>(
        "node-js-sidecar",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "sidecar", "typescript" });
```

### Aspire AppHost Registration (Task 2)

`AppHost/Program.cs` now:
1. Resolves `sidecarDir` via `builder.AppHostDirectory` + relative path to `ts-symbol-extractor`
2. Registers the Node.js sidecar as an Aspire resource: `builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")`
3. Injects `DOCAGENT_SIDECAR_DIR` into the McpServer: `.WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)`

`McpServer/Program.cs` picks up the env var using the established PathExpander pattern and binds it to `DocAgent:SidecarDir`, which flows into `DocAgentServerOptions.SidecarDir`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] AppHost AddHealthChecks() not available in Aspire.AppHost.Sdk**

- **Found during:** Task 2 — first build attempt of AppHost
- **Issue:** The plan specified `builder.Services.AddHealthChecks().AddCheck(...)` in the Aspire AppHost, but the `Aspire.AppHost.Sdk` does not include `Microsoft.Extensions.Diagnostics.HealthChecks` extension methods on `IServiceCollection`
- **Fix:** Removed the AppHost-level health check registration. The McpServer `/health` endpoint already returns HTTP 200 (Degraded is acceptable) for Aspire's probe. The `sidecar` variable is still declared but `.WithHealthCheck()` was also removed since the named check no longer exists
- **Files modified:** `src/DocAgent.AppHost/Program.cs`
- **Impact:** No functional difference — Aspire dashboard shows the sidecar resource; Node.js availability is surfaced via the McpServer `/health` endpoint instead

**2. [Rule 1 - Bug] SidecarDirEnvVar test used Unix path that PathExpander transforms on Windows**

- **Found during:** Task 2 test run
- **Issue:** Test used `/tmp/test-sidecar-dir` which `PathExpander.Expand` → `Path.GetFullPath` converts to `C:\tmp\test-sidecar-dir` on Windows, causing assertion mismatch
- **Fix:** Changed to `Path.GetTempPath() + "docagent-test-sidecar-dir"` (platform-appropriate prefix) and assertion to `.EndWith("docagent-test-sidecar-dir")` rather than exact equality
- **Files modified:** `tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs`

## Test Results

- 5 new tests added (NodeAvailabilityHealthCheckTests.cs)
- 631 non-stress tests pass after both tasks (zero regressions)
- Full solution builds with 0 warnings, 0 errors

## Self-Check

Files verified to exist:
- `src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs` — FOUND
- `tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs` — FOUND
- `src/DocAgent.AppHost/Program.cs` (contains AddNodeApp) — FOUND
- `src/DocAgent.McpServer/Program.cs` (contains DOCAGENT_SIDECAR_DIR) — FOUND

Commits verified:
- 7752114 — Task 1 commit — FOUND
- 04110c5 — Task 2 commit — FOUND

## Self-Check: PASSED
