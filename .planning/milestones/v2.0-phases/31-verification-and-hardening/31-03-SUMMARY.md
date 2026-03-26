---
phase: 31-verification-and-hardening
plan: 03
subsystem: ingestion
tags: [typescript, sidecar, security, path-leak, logging]

requires:
  - phase: 30-mcp-typescript-integration
    provides: TypeScript sidecar extractor and ingestion pipeline
provides:
  - Relative file paths in all TypeScript SymbolNode.Span.FilePath fields
  - Clean sidecar with only fatal error logging
  - Robustness test proving no absolute path leaks
affects: [ingestion, serving, security]

tech-stack:
  added: []
  patterns: [path.relative for all SourceSpan filePath values]

key-files:
  created: []
  modified:
    - src/ts-symbol-extractor/src/extractor.ts
    - src/ts-symbol-extractor/src/index.ts
    - src/ts-symbol-extractor/tests/golden-files/simple-project.json
    - tests/DocAgent.Tests/TypeScriptRobustnessTests.cs

key-decisions:
  - "Pass projectRoot into getSourceSpan and use path.relative for all span filePaths"
  - "Regenerate golden file after path format change (expected consequence of fix)"

patterns-established:
  - "All SourceSpan.filePath values must be project-relative, never absolute"

requirements-completed: [VERF-03]

duration: 12min
completed: 2026-03-25
---

# Phase 31 Plan 03: Sidecar Defect Fixes Summary

**Fixed absolute path leak in TypeScript SymbolNode.Span.FilePath and removed debug console.error statements from sidecar**

## Performance

- **Duration:** 12 min
- **Started:** 2026-03-25T19:35:11Z
- **Completed:** 2026-03-25T19:47:23Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- getSourceSpan now produces relative paths (e.g. "src/index.ts") instead of absolute OS paths (e.g. "C:/Users/james/.../src/index.ts")
- Removed two debug console.error statements from sidecar index.ts while preserving the fatal crash handler
- Added end-to-end robustness test validating no absolute paths appear in any ingested TypeScript snapshot

## Task Commits

Each task was committed atomically:

1. **Task 1: Fix absolute path leak in getSourceSpan and remove debug logging** - `72e903c` (fix)
2. **Task 2: Add robustness test asserting no absolute paths in Span.FilePath** - `cce89c2` (test)

## Files Created/Modified
- `src/ts-symbol-extractor/src/extractor.ts` - Added projectRoot parameter to getSourceSpan, uses path.relative for filePath
- `src/ts-symbol-extractor/src/index.ts` - Removed debug console.error statements (kept fatal handler)
- `src/ts-symbol-extractor/tests/golden-files/simple-project.json` - Regenerated with relative paths
- `tests/DocAgent.Tests/TypeScriptRobustnessTests.cs` - Added IngestTypeScriptAsync_produces_relative_file_paths_in_spans test

## Decisions Made
- Pass projectRoot into getSourceSpan rather than computing relative path at each call site — keeps the fix centralized
- Regenerated golden file rather than patching individual paths — ensures snapshot format is correct going forward

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Regenerated golden file for vitest**
- **Found during:** Task 1
- **Issue:** Golden file contained absolute paths matching old behavior; vitest snapshot comparison failed after fix
- **Fix:** Deleted stale golden file, re-ran vitest to bootstrap new golden file with relative paths
- **Files modified:** src/ts-symbol-extractor/tests/golden-files/simple-project.json
- **Verification:** All 10 vitest tests pass
- **Committed in:** 72e903c (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Expected consequence of the path format change. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- TypeScript sidecar now produces clean, secure snapshots with relative paths
- All robustness tests pass including the new path validation test
- 632 tests passing in full suite with 0 failures

---
*Phase: 31-verification-and-hardening*
*Completed: 2026-03-25*
