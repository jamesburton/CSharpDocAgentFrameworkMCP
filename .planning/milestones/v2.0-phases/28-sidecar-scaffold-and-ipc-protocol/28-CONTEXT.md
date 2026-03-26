# Phase 28: Sidecar Scaffold and IPC Protocol - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish the Node.js sidecar project (`ts-symbol-extractor`), NDJSON stdin/stdout IPC protocol, C# `TypeScriptIngestionService` process management, and Aspire startup validation. The sidecar accepts a tsconfig.json path and returns a stub SymbolGraphSnapshot-shaped response. Real symbol extraction is Phase 29.

</domain>

<decisions>
## Implementation Decisions

### Sidecar project layout
- Location: `src/ts-symbol-extractor/` alongside C# projects
- Module system: ESM with `"type": "module"` in package.json
- Sidecar source language: TypeScript (esbuild strips types during bundling)
- Bundle output: `dist/index.js` via esbuild, gitignored — built on demand, not committed
- Test runner: vitest
- TypeScript version: ~5.9.x pinned

### C# process lifecycle
- Timeout: 300 seconds (matches existing IngestionService timeout)
- Error handling: Throw typed `TypeScriptIngestionException` with exit code + stderr content; MCP tool catches and returns structured error response
- Stderr capture: Read asynchronously, forward each line to ILogger at Debug level; include stderr content in exception on failure
- Test seam: PipelineOverride pattern — `Func<string, Task<SymbolGraphSnapshot>>?` that bypasses process spawning, matching existing IngestionService test pattern

### Aspire integration
- Sidecar mode: On-demand spawn per request only — no Aspire resource registration for the sidecar itself
- Node.js detection: IHostedLifecycleService startup check via `node --version` — log warning if missing, don't crash (C# ingestion still works); TypeScriptIngestionService throws clear error if called without Node.js
- Version validation: Parse `node --version` output and require >= 22.x
- Auto build: Startup service runs `npm install && npm run build` in sidecar directory if `dist/index.js` is missing — ensures first run works without manual steps

### Claude's Discretion
- IPC contract details (NDJSON request/response schema) — research decided NDJSON over stdin/stdout; exact field shapes are implementation detail
- Internal sidecar source file organization (`src/index.ts` structure, helper modules)
- Exact esbuild configuration
- npm scripts structure (build, test, lint)
- Logging format on stderr

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Research established the key patterns:
- NDJSON protocol matches existing MCP stdio pattern
- Cold-start process isolation (spawn per request, OS reclaims memory on exit)
- All sidecar logging to stderr; stdout reserved exclusively for NDJSON responses
- `Aspire.Hosting.JavaScript` 13.1.2 (not deprecated `Aspire.Hosting.NodeJs`)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `IngestionService` (`src/DocAgent.McpServer/Ingestion/IngestionService.cs`): Orchestration pattern with per-path SemaphoreSlim, timeout, progress reporting — TypeScriptIngestionService should follow the same structure
- `PipelineOverride` seam pattern: Already used in RoslynSymbolGraphBuilder for MSBuild-free testing — copy for Node.js-free testing
- `IHostedLifecycleService` startup validation: Already used for config validation — extend for Node.js availability check
- `SymbolGraphSnapshot` record (`src/DocAgent.Core/Symbols.cs`): The exact JSON shape the sidecar must return
- `SnapshotStore` + `BM25SearchIndex`: Will consume the deserialized snapshot without modification

### Established Patterns
- All logging to stderr (MCP server already does this via `LogToStandardErrorThreshold = LogLevel.Trace`)
- PathAllowlist security validation before any pipeline work
- Deterministic output via `SymbolSorter` — C# side applies sorting after deserialization
- Content hash computed by `SnapshotStore`, not the extractor
- Per-path semaphore serialization for concurrent ingestion

### Integration Points
- `AppHost/Program.cs`: Add startup validation service registration (3-5 lines)
- `ServiceCollectionExtensions.cs`: Register TypeScriptIngestionService (2-3 lines)
- `DocAgentServerOptions.cs`: Add sidecar path configuration (1-2 lines)
- `Directory.Packages.props`: Add `Aspire.Hosting.JavaScript` 13.1.2 (1 line)

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 28-sidecar-scaffold-and-ipc-protocol*
*Context gathered: 2026-03-08*
