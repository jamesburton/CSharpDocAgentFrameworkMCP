---
phase: 06-analysis-hosting
plan: 03
subsystem: infra
tags: [aspire, apphost, health-checks, hosting, webapplication]

requires:
  - phase: 06-analysis-hosting
    provides: OpenTelemetry instrumentation wired in McpServer Program.cs
provides:
  - Aspire AppHost with docagent-mcp resource declaration
  - Config injection for DOCAGENT_ARTIFACTS_DIR and DOCAGENT_ALLOWLIST_PATHS
  - Health endpoint at /health for Aspire dashboard probing
affects: [06-analysis-hosting]

tech-stack:
  added: [Aspire.AppHost.Sdk 13.1.2]
  patterns: [DistributedApplication builder for Aspire orchestration, WebApplication for health endpoint alongside MCP stdio transport]

key-files:
  created: [src/DocAgent.AppHost/Program.cs]
  modified: [src/DocAgent.AppHost/DocAgent.AppHost.csproj, src/DocAgent.McpServer/Program.cs, src/DocAgent.McpServer/DocAgent.McpServer.csproj]

key-decisions:
  - "McpServer switched from Microsoft.NET.Sdk to Microsoft.NET.Sdk.Web for WebApplication + health endpoint support"
  - "Redundant packages (Hosting, Logging.Console, FileSystemGlobbing) removed — provided by Web SDK"

patterns-established:
  - "WebApplication.CreateBuilder coexists with MCP stdio transport — Kestrel listens on HTTP port for health, stdin/stdout for MCP JSON-RPC"

requirements-completed: [HOST-01]

duration: 15min
completed: 2026-02-27
---

# Phase 06 Plan 03: Aspire AppHost & Health Endpoint Summary

**Aspire AppHost with docagent-mcp resource, config injection, and /health endpoint for dashboard probing**

## Performance

- **Duration:** 15 min
- **Started:** 2026-02-27T19:45:12Z
- **Completed:** 2026-02-27T19:59:49Z
- **Tasks:** 1
- **Files modified:** 4

## Accomplishments
- AppHost upgraded from placeholder Microsoft.NET.Sdk to Aspire.AppHost.Sdk/13.1.2
- AppHost Program.cs declares docagent-mcp resource with environment variable injection (DOCAGENT_ARTIFACTS_DIR, DOCAGENT_ALLOWLIST_PATHS)
- Health endpoint configured via WithHttpEndpoint(port: 8089) and WithHttpHealthCheck("/health")
- McpServer switched to Microsoft.NET.Sdk.Web with WebApplication.CreateBuilder for /health MapHealthChecks support
- MCP stdio transport preserved alongside Kestrel HTTP listener (separate concerns: stdin/stdout for MCP, HTTP for health)
- All 157 existing tests pass

## Task Commits

1. **Task 1: Upgrade AppHost to Aspire SDK and add health endpoint to McpServer** - `e200662` (feat)

## Files Created/Modified
- `src/DocAgent.AppHost/Program.cs` (created) - DistributedApplication builder with docagent-mcp resource
- `src/DocAgent.AppHost/DocAgent.AppHost.csproj` - Aspire.AppHost.Sdk/13.1.2, IsAspireHost=true
- `src/DocAgent.McpServer/Program.cs` - WebApplication.CreateBuilder + MapHealthChecks("/health") + AddHealthChecks()
- `src/DocAgent.McpServer/DocAgent.McpServer.csproj` - Microsoft.NET.Sdk.Web, removed redundant package refs

## Decisions Made
- Switched McpServer SDK from Microsoft.NET.Sdk to Microsoft.NET.Sdk.Web to get WebApplication + health check support without manual HttpListener fallback
- Removed Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging.Console, and Microsoft.Extensions.FileSystemGlobbing package references — all provided transitively by Web SDK (NU1510 errors with TreatWarningsAsErrors)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] NU1510 redundant package errors with Web SDK**
- **Found during:** Task 1 (build verification)
- **Issue:** Switching to Microsoft.NET.Sdk.Web made Hosting, Logging.Console, and FileSystemGlobbing packages redundant; TreatWarningsAsErrors promoted NU1510 to errors
- **Fix:** Removed three redundant PackageReference entries from McpServer.csproj
- **Files modified:** src/DocAgent.McpServer/DocAgent.McpServer.csproj
- **Commit:** e200662

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Minor — same functionality, cleaner dependency graph.

## Issues Encountered
None.

## User Setup Required
None - Aspire SDK packages downloaded automatically during restore.

## Next Phase Readiness
- Phase 06 complete: Analyzers (06-01), OTel (06-02), Aspire hosting (06-03) all delivered
- `dotnet run --project src/DocAgent.AppHost` launches full system under Aspire orchestration

---
*Phase: 06-analysis-hosting*
*Completed: 2026-02-27*
