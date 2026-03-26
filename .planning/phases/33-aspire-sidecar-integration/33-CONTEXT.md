# Phase 33: Aspire Sidecar Integration - Context

**Gathered:** 2026-03-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Register the Node.js sidecar (`ts-symbol-extractor`) as a managed Aspire resource in AppHost so `dotnet run --project src/DocAgent.AppHost` includes the sidecar in Aspire orchestration with dashboard visibility, health checks, and centralized path configuration. This reverses the Phase 28 decision of "on-demand spawn only, no Aspire registration" to satisfy SIDE-04.

</domain>

<decisions>
## Implementation Decisions

### Sidecar resource model
- Register via `AddNodeApp()` from `Aspire.Hosting.JavaScript` package
- Aspire manages the sidecar lifecycle, shows it in the dashboard
- No startup ordering dependency — MCP server and sidecar start independently (parallel)
- Auto-build at startup: keep existing NodeAvailabilityValidator behavior that runs `npm install && npm run build` if `dist/index.js` is missing

### IPC model
- Claude's discretion: decide whether to keep spawn-per-request or shift to long-running Aspire-managed process based on what fits the existing architecture
- Key constraint: the current spawn-per-request model was chosen for cold-start isolation (no memory leaks, OS reclaims)

### Startup and failure behavior
- Degrade gracefully when Node.js is missing — AppHost starts, sidecar resource shows 'unhealthy' in dashboard, MCP server works for C# ingestion only
- Fail immediately with clear TypeScriptIngestionException on sidecar crash — no retry (keep current behavior)
- Keep NodeAvailabilityValidator as IHostedLifecycleService for standalone MCP server mode; Aspire health checks supplement it when running under AppHost

### Health check integration
- Aspire health check: `node --version` check to verify Node.js availability (matches existing NodeAvailabilityValidator logic)
- Sidecar appears healthy/unhealthy in Aspire dashboard based on Node.js presence
- MCP server /health endpoint: include sidecar status — return 'degraded' when Node.js is missing (C# ingestion still works, TS ingestion unavailable)

### Path centralization
- Centralize sidecar path in `DocAgentServerOptions.SidecarDir`
- Aspire communicates path via `DOCAGENT_SIDECAR_DIR` environment variable from AppHost (consistent with existing ARTIFACTS_DIR / ALLOWED_PATHS patterns)
- Keep existing fallback chains as standalone-mode fallback — when SidecarDir is not set and not running under Aspire, dev convenience paths still work
- Both TypeScriptIngestionService and NodeAvailabilityValidator read from the same centralized option

### Claude's Discretion
- Exact AddNodeApp() configuration and arguments
- Whether to refactor TypeScriptIngestionService to communicate with a long-running Aspire process or keep spawn-per-request with Aspire providing validation/path only
- Health check implementation details (custom IHealthCheck vs Aspire built-in)
- How to expose sidecar status in the existing /health endpoint (JSON shape, status codes)

</decisions>

<specifics>
## Specific Ideas

- The environment variable pattern (`DOCAGENT_SIDECAR_DIR`) matches the existing `DOCAGENT_ARTIFACTS_DIR` and `DOCAGENT_ALLOWED_PATHS` in AppHost Program.cs — keep it consistent
- Phase 28 CONTEXT noted `Aspire.Hosting.JavaScript` 13.1.2 (not deprecated `Aspire.Hosting.NodeJs`) — verify this is still the correct package
- The /health endpoint should include a field like `"typescriptSupport": "available"` or `"typescriptSupport": "unavailable"` so consumers know TS ingestion capability

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `NodeAvailabilityValidator` (`src/DocAgent.McpServer/Validation/NodeAvailabilityValidator.cs`): Already implements Node.js version check, sidecar build, path discovery — core logic can be reused for Aspire health check
- `DocAgentServerOptions` (`src/DocAgent.McpServer/DocAgentServerOptions.cs`): Already has SidecarDir and NodeExecutable properties
- `AppHost/Program.cs`: Simple 11-line Aspire app — clean extension point for sidecar resource

### Established Patterns
- Environment variables for AppHost→MCP config: `DOCAGENT_ARTIFACTS_DIR`, `DOCAGENT_ALLOWED_PATHS`
- Health endpoint at `/health` with `WithHttpHealthCheck("/health")`
- Non-fatal Node.js detection: log warning, don't crash
- Sidecar path fallback: configured → AppContext.BaseDirectory → cwd

### Integration Points
- `src/DocAgent.AppHost/Program.cs`: Add `AddNodeApp()` registration (~5-10 lines)
- `src/DocAgent.AppHost/DocAgent.AppHost.csproj`: Add `Aspire.Hosting.JavaScript` package reference
- `src/Directory.Packages.props`: Add package version entry
- `src/DocAgent.McpServer/DocAgentServerOptions.cs`: Ensure SidecarDir reads from `DOCAGENT_SIDECAR_DIR`
- Health endpoint: extend to include sidecar status

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 33-aspire-sidecar-integration*
*Context gathered: 2026-03-26*
