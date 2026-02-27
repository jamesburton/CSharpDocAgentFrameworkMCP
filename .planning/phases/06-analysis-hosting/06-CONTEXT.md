# Phase 6: Analysis + Hosting - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Roslyn diagnostic analyzers that enforce documentation quality at build time (doc parity, suspicious edits, coverage policy), plus Aspire app host wiring with OpenTelemetry observability for the MCP server. Ingestion triggering is Phase 8 — this phase only serves and observes.

</domain>

<decisions>
## Implementation Decisions

### Analyzer Severity & Defaults
- Default diagnostic severity: **Warning** (not Error)
- Teams can escalate to Error via .editorconfig or project-level severity overrides
- Target: **all public members** (types, methods, properties, events, fields)
- Suppression: support **both** a custom `[ExcludeFromDocCoverage]` attribute and standard `#pragma warning disable` / `[SuppressMessage]`

### Suspicious Edit Detection (ANLY-02)
- Scope: **signature changes + observable contracts**
- Signature: parameter types, return type, accessibility, generic constraints
- Observable contracts: new throw statements, nullability annotation changes, attribute changes
- Goal: detect semantic changes where docs/tests weren't updated alongside

### Doc Coverage Policy (ANLY-03)
- Default threshold: **80%** of public symbols must have `<summary>` documentation
- Measurement scope: **per-project** (each project has its own ratio)
- Reporting: **summary line + list of undocumented symbols** (actionable output)
- Configuration: support **both** MSBuild property (`<DocCoverageThreshold>80</DocCoverageThreshold>`) and EditorConfig

### Telemetry Verbosity
- **Default mode:** tool name, duration, success/error status, symbol count returned
- **Verbose mode:** full input parameters, output size, result count, plus nested pipeline stage spans (ingestion/indexing/query resolution)
- Verbose mode is the default in Debug/Development builds
- Custom metrics counters (tool call count, error count) deferred — bring forward if needed for diagnostics during this phase

### Telemetry Exporter
- OTLP exporter to Aspire dashboard (standard OpenTelemetry pipeline)
- Can redirect to any OTLP-compatible backend via configuration

### Aspire Resource
- MCP server appears as named resource `docagent-mcp` with health check endpoint
- Green/red status visible at a glance in Aspire dashboard

### Aspire Configuration
- AppHost is **primary source of truth** for artifacts directory and allowlist paths via Aspire resource config
- Environment variables supported as fallback (DOCAGENT_ARTIFACTS_DIR etc.)
- MCP server only at startup — no auto-ingestion (Phase 8 concern)

### Structured Logging
- Wire ILogger through OpenTelemetry log exporter so logs appear in Aspire dashboard alongside traces
- Stderr still used for MCP transport isolation (JSON-RPC framing safety)

### Claude's Discretion
- Analyzer diagnostic IDs and naming conventions
- Health check implementation details (what constitutes "healthy")
- Exact OpenTelemetry span naming conventions
- How verbose mode is toggled (config flag, environment variable, etc.)
- Internal Roslyn syntax analysis approach for detecting observable contract changes

</decisions>

<specifics>
## Specific Ideas

- Verbose telemetry should be the default in debug/dev builds so developers get full pipeline visibility during development
- Coverage gate should output the list of undocumented symbols so developers know exactly what to fix
- Both MSBuild and EditorConfig configuration surfaces for maximum flexibility in different project setups

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-analysis-hosting*
*Context gathered: 2026-02-27*
