---
phase: 14-solution-ingestion-pipeline
plan: 02
subsystem: api
tags: [mcp, ingestion, solution, dotnet, security, pathallowlist, di]

requires:
  - phase: 14-01
    provides: ISolutionIngestionService, SolutionIngestionService, SolutionIngestionResult, ProjectIngestionStatus

provides:
  - ingest_solution MCP tool method in IngestionTools with PathAllowlist security gate
  - ISolutionIngestionService registered as singleton in AddDocAgent DI extension
  - 5 tool-level tests covering security and response shape for ingest_solution

affects: [phase-15-query-tools, phase-16-snapshot-diffing]

tech-stack:
  added: []
  patterns:
    - "Tool method pattern: validate path → allowlist gate → extract progress token → delegate to service → serialize JSON"
    - "ISolutionIngestionService stub pattern for tool-level tests (no MSBuild, no DI container)"
    - "IngestionTools constructor receives both IIngestionService and ISolutionIngestionService"

key-files:
  created:
    - tests/DocAgent.Tests/SolutionIngestionToolTests.cs
  modified:
    - src/DocAgent.McpServer/Tools/IngestionTools.cs
    - src/DocAgent.McpServer/ServiceCollectionExtensions.cs
    - tests/DocAgent.Tests/IngestionToolTests.cs

key-decisions:
  - "IngestSolution mirrors IngestProject pattern exactly: same security gate message, same progress token extraction, same error handling"
  - "Response JSON includes per-project array with {name, filePath, status, reason, nodeCount, chosenTfm} fields matching SolutionIngestionResult shape"
  - "ISolutionIngestionService stub in tests does not share code with SolutionIngestionService to keep tests isolated from MSBuild"

patterns-established:
  - "MCP tool constructor: all security/logging deps injected, each tool method self-contained"
  - "Tool tests use null! for McpServer+RequestContext (safe when progressToken path not exercised)"

requirements-completed: [INGEST-01, INGEST-06]

duration: 15min
completed: 2026-03-01
---

# Phase 14 Plan 02: ingest_solution MCP Tool Wiring Summary

**ingest_solution MCP tool wired into IngestionTools with PathAllowlist security gate, DI singleton registration, and 5 tool-level tests for security denial and full response-shape validation**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-01T18:15:00Z
- **Completed:** 2026-03-01T18:30:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Added `IngestSolution` method to `IngestionTools` with identical security pattern as `IngestProject` (PathAllowlist gate, progress token extraction, telemetry activity, error handling)
- Added `ISolutionIngestionService` field + constructor injection to `IngestionTools`, DI registered as singleton in `AddDocAgent`
- Created `SolutionIngestionToolTests.cs` with 5 tests: allowlist denial, allowlist pass, null/empty path, full response shape (9 fields), exception handling

## Task Commits

1. **Task 1: Add IngestSolution tool method and DI wiring** - `6ca3245` (feat)
2. **Task 2: Add tool-level tests for security and response** - `692e68e` (test)

## Files Created/Modified

- `src/DocAgent.McpServer/Tools/IngestionTools.cs` - Added ISolutionIngestionService field + constructor injection + IngestSolution method (~90 new lines)
- `src/DocAgent.McpServer/ServiceCollectionExtensions.cs` - Added singleton registration for ISolutionIngestionService
- `tests/DocAgent.Tests/SolutionIngestionToolTests.cs` - New: 5 tool-level tests with StubSolutionIngestionService
- `tests/DocAgent.Tests/IngestionToolTests.cs` - Updated IngestionTools constructor call + added StubSolutionIngestionService inner class

## Decisions Made

- IngestSolution mirrors IngestProject exactly: same "Path is not in the configured allow list." message, same progress token extraction, same try/catch structure — enforces consistent security behaviour across all ingestion tools
- Response JSON serializes per-project array inline (LINQ Select projection) rather than a separate DTO to keep serialization co-located with tool method

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated IngestionToolTests.cs for new constructor signature**
- **Found during:** Task 2 (running tests after adding ISolutionIngestionService parameter)
- **Issue:** Existing `IngestionToolTests` called `new IngestionTools(svc, allowlist, logger)` — 3 args; constructor now requires 4
- **Fix:** Added `StubSolutionIngestionService` inner class to `IngestionToolTests` and updated `CreateTools` factory to pass it
- **Files modified:** tests/DocAgent.Tests/IngestionToolTests.cs
- **Verification:** All pre-existing IngestionToolTests passed after fix
- **Committed in:** 692e68e (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - constructor arity change cascading into existing test)
**Impact on plan:** Necessary correctness fix. No scope creep.

## Issues Encountered

None beyond the constructor arity cascade documented above.

## Next Phase Readiness

- `ingest_solution` MCP tool is fully wired and discoverable by MCP SDK clients
- ISolutionIngestionService registered in DI — ready for integration tests against real .sln files
- Phase 15 (query tools) can reference snapshotId from `ingest_solution` responses

---
*Phase: 14-solution-ingestion-pipeline*
*Completed: 2026-03-01*
