# Phase 8: Ingestion Runtime Trigger - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire the full ingestion pipeline (discover → parse → snapshot → index) as an `ingest_project` MCP tool so users can trigger ingestion at runtime and immediately query results via existing MCP tools. Respects PathAllowlist security boundary. E2E: tool call → discover → parse → snapshot → index → query succeeds in a single session.

</domain>

<decisions>
## Implementation Decisions

### Tool interface design
- Accepts `.sln`, `.slnx`, and `.csproj` paths (both solution and project files)
- Parameters: path (required), include/exclude glob patterns (optional), force-reindex flag (optional)
- Validates path against PathAllowlist upfront before any work begins — fail fast with clear error

### Ingestion feedback
- Rich summary response on success: snapshot ID, symbol count, project count, duration, warnings
- Streaming progress at per-stage granularity: "Discovering...", "Parsing (N files)...", "Building snapshot...", "Indexing..."
- Warnings (skipped files, missing XML docs) included in both the tool response and server logs

### Concurrency & state
- Parallel ingestion allowed — multiple projects can be ingested concurrently
- Multi-project index: each project gets its own snapshot, all are queryable simultaneously (search_symbols searches across all)
- Snapshot history preserved — keep previous snapshots (enables future diff_snapshots), but only latest is queryable by default
- Atomic swap: new snapshot becomes queryable all at once after full index build, no partial results during ingestion

### Error behavior
- Partial failure tolerance: skip unparseable files, continue ingesting what we can, include skipped files in warnings
- Invalid/missing path or no parseable files: return tool error (isError: true) with clear message
- If ingestion succeeds but indexing fails: store the snapshot (no data loss), report index failure in response
- Configurable timeout with a reasonable default (e.g., 5 minutes) to prevent hanging on very large solutions

### Claude's Discretion
- Exact streaming progress mechanism (MCP protocol-appropriate approach)
- Default timeout value
- Internal pipeline orchestration and DI wiring

</decisions>

<specifics>
## Specific Ideas

- Glob patterns for include/exclude (standard developer-familiar format like `**/*.cs`, `!**/Tests/**`)
- Per-stage progress updates feel natural for pipeline stages that already exist in the architecture
- Snapshot history enables future `diff_snapshots` MCP tool without rework

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-ingestion-runtime-trigger*
*Context gathered: 2026-02-27*
