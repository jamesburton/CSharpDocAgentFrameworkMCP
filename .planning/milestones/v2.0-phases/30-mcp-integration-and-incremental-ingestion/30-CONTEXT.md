# Phase 30: MCP Integration and Incremental Ingestion - Context

**Gathered:** 2026-03-25
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire TypeScript symbol extraction into the MCP server as queryable tools: `ingest_typescript` for single-project ingestion with SHA-256 incremental caching, `ingest_typescript_workspace` for monorepo ingestion, and ensure all 14 existing query/change-intelligence tools work seamlessly with TypeScript snapshots. CamelCase tokenization in BM25 search must handle both PascalCase and camelCase naming conventions.

</domain>

<decisions>
## Implementation Decisions

### Incremental Detection Scope
- File scope: Claude's discretion (recursive scan vs tsconfig parsing)
- Any file hash change triggers full re-extraction (not per-file incremental) — TypeScript's cross-file type system makes partial extraction unreliable
- Include `tsconfig.json` hash in manifest — config changes (strict, target, paths) affect type resolution
- Include `package-lock.json` hash if present — dependency type definitions affect extraction
- Manifest scope covers source files + config + lockfile

### Tool Compatibility
- Unified tool surface — all 14 existing MCP tools (search_symbols, get_symbol, get_references, etc.) work identically on TypeScript snapshots
- Mixed results by default — polyglot queries return both C# and TS results ranked by relevance; filter with `project` parameter to narrow to one language
- Map TypeScript kinds to existing SymbolKinds (function → Method, interface → Type, type alias → Type, enum → Type) — no new enum values
- explain_project and get_doc_coverage include TypeScript projects with JSDoc coverage metrics

### Sidecar IPC and Output Model
- Keep temp file approach for large extraction output (Phase 28 pattern) — avoids stdout buffer limits
- Structured error responses with diagnostic category (sidecar_timeout, parse_error, tsconfig_invalid) + human message + optional diagnostics array — no raw stderr passthrough
- Configurable sidecar timeout via `TypeScriptSidecarTimeoutSeconds` in DocAgentServerOptions (default: 120s)
- Match C# progress notification pattern — send MCP progress notifications during sidecar run (starting, extracting, indexing)

### Re-ingestion UX
- Add `forceReindex` parameter to `ingest_typescript` — matches `ingest_project` API shape
- On incremental skip: return cached snapshot info with `skipped: true` and `reason: "no changes detected"` — caller knows no work was needed
- Response envelope: snapshotId, symbolCount, durationMs, warnings, indexError (no changed files list)
- Add `ingest_typescript_workspace` tool for monorepo support — accepts root directory, finds all tsconfig.json files, ingests each as separate project (parallels ingest_solution for .NET)

### Claude's Discretion
- File scope detection strategy (recursive scan vs tsconfig parsing)
- CamelCaseAnalyzer adjustments if any needed for TypeScript conventions
- Internal manifest file format and storage location
- Progress notification granularity and phase naming
- ingest_typescript_workspace discovery strategy (how to find tsconfig files in monorepo)

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TypeScriptIngestionService`: Already implements sidecar spawn + temp file read + snapshot building
- `CamelCaseAnalyzer` + `CamelCaseTokenizer`: Handles both PascalCase and camelCase splitting in Lucene index
- `PathAllowlist`: Security enforcement already wired into `ingest_typescript` tool
- `DocAgentTelemetry.Source`: Activity tracing pattern ready for TypeScript tools
- `IngestionTools.cs`: All three ingestion tools already registered with MCP server
- `PipelineOverride` test seam: From Phase 28, allows injecting mock sidecar output in tests

### Established Patterns
- Progress callback pattern: `Func<int, int, string, Task>` with `progressToken` extraction from request context
- Error response: `ErrorJson(errorType, message)` helper for consistent error shapes
- JSON serialization: `JsonNamingPolicy.CamelCase` with `WriteIndented = false`
- Ingestion result envelope: `snapshotId`, `symbolCount`, `durationMs`, `warnings`, `indexError`

### Integration Points
- `DocAgentServerOptions`: Add `TypeScriptSidecarTimeoutSeconds` and `TypeScriptFileExtensions` options
- `ISearchIndex`: TypeScript snapshots feed into same Lucene index for unified search
- `SnapshotStore`: TypeScript snapshots stored alongside C# snapshots with language-aware keys
- Solution-level tools (`explain_solution`, `diff_solution_snapshots`): May need awareness of mixed-language snapshots

</code_context>

<specifics>
## Specific Ideas

- ingest_typescript_workspace should parallel the ingest_solution pattern — discover projects, ingest each, return per-project summary
- The `skipped` flag in incremental response is important for caller transparency — agents should know when no work happened
- Structured error categories (sidecar_timeout, parse_error, tsconfig_invalid) enable programmatic error handling by MCP consumers

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 30-mcp-integration-and-incremental-ingestion*
*Context gathered: 2026-03-25*
