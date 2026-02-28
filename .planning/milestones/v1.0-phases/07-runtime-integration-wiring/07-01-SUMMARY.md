---
phase: 07-runtime-integration-wiring
plan: 01
subsystem: infra
tags: [dotnet, di, mcp-server, aspire, dependency-injection]

requires:
  - phase: 04-query-facade
    provides: KnowledgeQueryService, IKnowledgeQueryService, SnapshotStore, BM25SearchIndex
  - phase: 05-mcp-server-security
    provides: DocAgentServerOptions, PathAllowlist, AuditLogger, Program.cs scaffold
provides:
  - AddDocAgent() IServiceCollection extension method registering all core services
  - DocAgentServerOptions.ArtifactsDir property with DOCAGENT_ARTIFACTS_DIR env var support
  - SymbolNotFoundException exception type in DocAgent.Core
  - Startup index loading from disk in Program.cs
affects: [07-02, future-mcp-tools]

tech-stack:
  added: []
  patterns:
    - "Closure-based singleton path resolution to prevent SnapshotStore/BM25SearchIndex path divergence"
    - "Env var injection into IConfiguration before IOptions is built (DOCAGENT_ARTIFACTS_DIR)"
    - "Startup warm-up: resolve singleton services after Build() and load existing snapshot"

key-files:
  created:
    - src/DocAgent.McpServer/ServiceCollectionExtensions.cs
    - src/DocAgent.Core/SymbolNotFoundException.cs
  modified:
    - src/DocAgent.McpServer/Config/DocAgentServerOptions.cs
    - src/DocAgent.McpServer/Program.cs

key-decisions:
  - "SnapshotStore and BM25SearchIndex registered as singletons; KnowledgeQueryService as scoped — matches constructor dependency requirements"
  - "Path resolved once via closure in GetDir() to ensure both singletons share same canonical artifacts directory"
  - "DOCAGENT_ARTIFACTS_DIR env var injected into IConfiguration before Configure<DocAgentServerOptions>() — ensures env var wins over appsettings"
  - "Startup uses IndexAsync (not LoadIndexAsync) — idempotent due to BM25 freshness check"

patterns-established:
  - "AddDocAgent() extension: configuration-driven service registration pattern for McpServer layer"
  - "Startup warm-up: app.Services.GetRequiredService<T>() after Build() for side-effect initialization"

requirements-completed: [QURY-01, INDX-01, INDX-03, INGS-04, MCPS-01, MCPS-02, MCPS-04, MCPS-05]

duration: 15min
completed: 2026-02-27
---

# Phase 7 Plan 01: Runtime Integration Wiring Summary

**DI gap closed: AddDocAgent() extension wires SnapshotStore (singleton), BM25SearchIndex as ISearchIndex (singleton), and KnowledgeQueryService (scoped) into the MCP server with ArtifactsDir config and startup index loading**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-02-27T13:05:00Z
- **Completed:** 2026-02-27T13:20:37Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Created `AddDocAgent()` IServiceCollection extension registering all three core runtime services
- Added `ArtifactsDir` to `DocAgentServerOptions` with DOCAGENT_ARTIFACTS_DIR env var support
- Created `SymbolNotFoundException` in DocAgent.Core for use in 07-02 MCP tools
- Updated Program.cs to call AddDocAgent() and load existing snapshot index at startup

## Task Commits

1. **Task 1: Add ArtifactsDir to DocAgentServerOptions + SymbolNotFoundException** - `678d17c` (feat)
2. **Task 2: Create ServiceCollectionExtensions + wire Program.cs** - `e61ec35` (feat)

## Files Created/Modified
- `src/DocAgent.McpServer/ServiceCollectionExtensions.cs` - AddDocAgent() extension method with closure-based path resolution
- `src/DocAgent.Core/SymbolNotFoundException.cs` - Domain exception for missing symbol lookups
- `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` - Added ArtifactsDir property
- `src/DocAgent.McpServer/Program.cs` - Wired AddDocAgent(), env var injection, startup index loading

## Decisions Made
- SnapshotStore and BM25SearchIndex as singletons, KnowledgeQueryService as scoped (matches constructor requirements and session semantics)
- Path resolved once via closure to prevent divergence between the two singletons (Research Pitfall 1)
- DOCAGENT_ARTIFACTS_DIR env var injected into IConfiguration directly before IOptions is built, ensuring env var precedence
- Startup uses `IndexAsync` (idempotent, BM25 freshness check) rather than `LoadIndexAsync`

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- DI wiring complete: MCP server will no longer crash at runtime with unresolved IKnowledgeQueryService
- SymbolNotFoundException available for 07-02 MCP tool implementations
- ArtifactsDir configurable via appsettings.json, env var, or CLI
- All 111 existing unit tests pass

---
*Phase: 07-runtime-integration-wiring*
*Completed: 2026-02-27*
