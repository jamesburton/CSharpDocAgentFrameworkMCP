---
phase: 06-analysis-hosting
plan: 02
subsystem: infra
tags: [opentelemetry, otlp, tracing, metrics, observability, aspire]

requires:
  - phase: 05-mcp-server-security
    provides: DocTools MCP tool handlers and Program.cs DI wiring
provides:
  - Static ActivitySource for DocAgent.McpServer tracing
  - Per-tool Activity spans with tool.name, result_count, status tags
  - OTLP exporter wiring for Aspire dashboard visibility
  - Structured log export through OpenTelemetry
affects: [06-analysis-hosting]

tech-stack:
  added: [OpenTelemetry.Extensions.Hosting 1.15.0, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0, OpenTelemetry.Instrumentation.Runtime 1.15.0]
  patterns: [ActivitySource span wrapping for MCP tools, VerboseMode environment-gated telemetry]

key-files:
  created: [src/DocAgent.McpServer/Telemetry/DocAgentTelemetry.cs]
  modified: [src/DocAgent.McpServer/Program.cs, src/DocAgent.McpServer/Tools/DocTools.cs, src/DocAgent.McpServer/DocAgent.McpServer.csproj, Directory.Packages.props]

key-decisions:
  - "Per-signal AddOtlpExporter() instead of UseOtlpExporter() — 1.15.0 API compatibility"

patterns-established:
  - "Activity span pattern: StartActivity + SetTag + SetStatus for every MCP tool"
  - "VerboseMode static flag gated by IsDevelopment() or DOCAGENT_TELEMETRY_VERBOSE env var"

requirements-completed: [HOST-02]

duration: 10min
completed: 2026-02-27
---

# Phase 06 Plan 02: OpenTelemetry Instrumentation Summary

**OpenTelemetry tracing with per-tool Activity spans, OTLP export, and verbose mode for all 5 MCP tools**

## Performance

- **Duration:** 10 min
- **Started:** 2026-02-27T14:00:48Z
- **Completed:** 2026-02-27T14:10:48Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- All 5 MCP tool methods (SearchSymbols, GetSymbol, GetReferences, DiffSnapshots, ExplainProject) produce Activity spans with tool.name, tool.result_count, and success/error status
- OTLP exporter wired for both tracing and metrics, readable by Aspire dashboard
- Structured log export through OpenTelemetry logging provider
- VerboseMode auto-enabled in Development, adds input parameter tags to spans

## Task Commits

Each task was committed atomically:

1. **Task 1: Add OpenTelemetry packages, create DocAgentTelemetry, and wire OTLP in Program.cs** - `51fb651` (feat)
2. **Task 2: Instrument all five DocTools methods with Activity spans** - `ae84e95` (feat)

## Files Created/Modified
- `src/DocAgent.McpServer/Telemetry/DocAgentTelemetry.cs` - Static ActivitySource and VerboseMode flag
- `src/DocAgent.McpServer/Program.cs` - OpenTelemetry registration with OTLP exporters and log wiring
- `src/DocAgent.McpServer/Tools/DocTools.cs` - Activity span instrumentation on all 5 tool methods
- `src/DocAgent.McpServer/DocAgent.McpServer.csproj` - OTel package references
- `Directory.Packages.props` - OTel package version pins

## Decisions Made
- Used per-signal `AddOtlpExporter()` on TracerProviderBuilder and MeterProviderBuilder instead of `UseOtlpExporter()` on OpenTelemetryBuilder, as the latter is not available in OTel 1.15.0

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] UseOtlpExporter API not available in OTel 1.15.0**
- **Found during:** Task 1 (OTLP wiring)
- **Issue:** Plan specified `UseOtlpExporter()` on OpenTelemetryBuilder but this method does not exist in 1.15.0
- **Fix:** Used per-signal `AddOtlpExporter()` on tracing and metrics builders instead
- **Files modified:** src/DocAgent.McpServer/Program.cs
- **Verification:** Build succeeds
- **Committed in:** 51fb651

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Minor API surface change, same OTLP functionality delivered.

## Issues Encountered
- Pre-existing analyzer test failure (DocParityAnalyzerTests.PublicClassWithExcludeAttribute_NoDiagnostic) unrelated to this plan — not addressed per scope boundary rules.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Telemetry infrastructure in place for all MCP tools
- Ready for Aspire dashboard integration (set OTEL_EXPORTER_OTLP_ENDPOINT)

---
*Phase: 06-analysis-hosting*
*Completed: 2026-02-27*
