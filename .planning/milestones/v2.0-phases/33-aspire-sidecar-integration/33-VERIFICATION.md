---
phase: 33-aspire-sidecar-integration
verified: 2026-03-26T00:00:00Z
status: passed
score: 4/4 must-haves verified
re_verification: false
gaps: []
human_verification:
  - test: "Aspire dashboard sidecar visibility"
    expected: "Running `dotnet run --project src/DocAgent.AppHost` shows a 'ts-sidecar' resource in the Aspire dashboard alongside 'docagent-mcp'"
    why_human: "Requires the Aspire dashboard to be running; cannot verify visually via grep or build"
---

# Phase 33: Aspire Sidecar Integration Verification Report

**Phase Goal:** Register the Node.js sidecar as a managed Aspire resource in AppHost so `dotnet run --project src/DocAgent.AppHost` starts and orchestrates the sidecar
**Verified:** 2026-03-26
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | `dotnet run --project src/DocAgent.AppHost` starts Node.js sidecar as a managed Aspire resource visible in the dashboard | VERIFIED (human for dashboard UI) | `AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")` present in `AppHost/Program.cs` line 10; `AppHost` builds with 0 errors |
| 2 | NodeAvailabilityHealthCheck returns Degraded (not Unhealthy) when Node.js is absent, so /health returns HTTP 200 | VERIFIED | `NodeAvailabilityHealthCheck.cs` returns `HealthCheckResult.Degraded(...)` on all failure paths; `CheckHealthAsync_returns_Degraded_when_node_not_available` + 2 more Degraded tests pass (5/5 tests pass) |
| 3 | DOCAGENT_SIDECAR_DIR environment variable from AppHost flows into McpServer DocAgentServerOptions.SidecarDir | VERIFIED | AppHost `Program.cs` line 17: `.WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)`; McpServer `Program.cs` lines 48-51: PathExpander pickup into `builder.Configuration["DocAgent:SidecarDir"]`; `SidecarDirEnvVar_WhenSet_BindsToDocAgentServerOptions` test passes |
| 4 | Existing NodeAvailabilityValidator behavior is unchanged — auto-build still works in standalone mode | VERIFIED | Full solution builds with 0 warnings, 0 errors; `ParseNodeVersion` promoted to `public static` (non-breaking); all 631+ non-stress tests pass |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/DocAgent.AppHost/Program.cs` | `AddNodeApp()` sidecar registration with `DOCAGENT_SIDECAR_DIR` injection | VERIFIED | Line 10: `builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")`. Line 17: `.WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)`. 22 lines, substantive. |
| `src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs` | `IHealthCheck` returning Degraded when Node.js is missing | VERIFIED | 79 lines; exports `NodeAvailabilityHealthCheck`; Degraded returned on null output, unsupported version, and all exceptions |
| `src/DocAgent.McpServer/Program.cs` | `DOCAGENT_SIDECAR_DIR` env var pickup into configuration | VERIFIED | Lines 48-51 match the `DOCAGENT_ARTIFACTS_DIR` pickup pattern exactly |
| `tests/DocAgent.Tests/NodeAvailabilityHealthCheckTests.cs` | Unit tests for health check Degraded/Healthy behavior and env var binding | VERIFIED | 119 lines (exceeds 40-line minimum); 5 tests: absent, unsupported, healthy, exception, env var binding — all pass |
| `Directory.Packages.props` | `Aspire.Hosting.JavaScript Version="13.1.2"` entry | VERIFIED | Line 9: `<PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.1.2" />` |
| `src/DocAgent.AppHost/DocAgent.AppHost.csproj` | `Aspire.Hosting.JavaScript` package reference | VERIFIED | Line 11: `<PackageReference Include="Aspire.Hosting.JavaScript" />` |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `AppHost/Program.cs` | `McpServer/Program.cs` | `WithEnvironment("DOCAGENT_SIDECAR_DIR", sidecarDir)` | WIRED | AppHost line 17 injects the env var; McpServer lines 48-51 consume it |
| `McpServer/Program.cs` | `McpServer/Config/DocAgentServerOptions.cs` | `builder.Configuration["DocAgent:SidecarDir"]` binding | WIRED | Line 51 sets config key; `DocAgentServerOptions.SidecarDir` declared on line 28 of Options class; `services.Configure<DocAgentServerOptions>` wires binding |
| `NodeAvailabilityHealthCheck.cs` | `NodeAvailabilityValidator.cs` | `NodeAvailabilityValidator.ParseNodeVersion` | WIRED | `ParseNodeVersion` promoted to `public static` (confirmed at line 29 of Validator); called at line 40 of HealthCheck |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|---------|
| SIDE-04 | 33-01-PLAN.md | Aspire AppHost registers Node.js sidecar via `AddNodeApp()` with startup validation for Node.js availability | SATISFIED | `AddNodeApp("ts-sidecar", ...)` in AppHost/Program.cs. Startup validation delivered via McpServer `/health` (Degraded) rather than an AppHost-level health lambda — acceptable deviation because `Aspire.AppHost.Sdk` does not expose `AddHealthChecks()` extensions. The McpServer `NodeAvailabilityHealthCheck` is registered with `failureStatus: Degraded`, so `/health` always returns HTTP 200 while surfacing Node.js availability in the Aspire dashboard health probe. |

No orphaned requirements: REQUIREMENTS.md traceability table maps SIDE-04 exclusively to Phase 33 and marks it Complete.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `NodeAvailabilityHealthCheck.cs` | 68 | `return null` | Info | Legitimate null guard for `Process.Start` returning null — not a stub |

No blockers or warnings found.

### Deviation Assessment: AppHost Health Check Removal

The plan specified a health check lambda (`builder.Services.AddHealthChecks().AddCheck(...)`) in `AppHost/Program.cs`. The executor removed this because `Aspire.AppHost.Sdk` does not include `Microsoft.Extensions.Diagnostics.HealthChecks` extension methods on `IServiceCollection`. The `.WithHealthCheck("node-js-available")` call on the sidecar resource was also removed (no named check to attach).

**Verdict: Acceptable.** SIDE-04 requires "startup validation for Node.js availability" — this is fully provided by the McpServer's `NodeAvailabilityHealthCheck` (registered with `failureStatus: Degraded`), which the Aspire dashboard probes via the `.WithHttpHealthCheck("/health")` on the `docagent-mcp` resource. The sidecar resource (`ts-sidecar`) IS registered in the Aspire orchestration graph via `AddNodeApp()`. The AppHost-level health check was an implementation detail in the plan, not a contract requirement in SIDE-04.

### Human Verification Required

#### 1. Aspire Dashboard Sidecar Visibility

**Test:** Run `dotnet run --project src/DocAgent.AppHost` and open the Aspire dashboard (default: http://localhost:15022).
**Expected:** A resource named `ts-sidecar` is visible alongside `docagent-mcp`. The `ts-sidecar` resource may show as starting or degraded if Node.js dist files are not built, but it must appear as a managed resource.
**Why human:** Requires the Aspire runtime and dashboard to be running; the resource registration is verified in code but the dashboard rendering cannot be tested programmatically.

### Gaps Summary

No gaps. All four observable truths are verified. The deviation from the original plan (AppHost-level health check lambda removed) is acceptable — the SIDE-04 contract is fully satisfied via `AddNodeApp()` registration and McpServer `/health` endpoint Degraded signaling.

---

_Verified: 2026-03-26_
_Verifier: Claude (gsd-verifier)_
