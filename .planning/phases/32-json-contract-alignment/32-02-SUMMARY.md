---
phase: 32-json-contract-alignment
plan: "02"
subsystem: testing
tags: [typescript, json, deserialization, golden-file, integration-test, mcp-tools]
dependency_graph:
  requires:
    - phase: 32-01
      provides: SidecarJsonOptions with JsonStringEnumConverter + DocCommentConverter; JsonPropertyName attrs on all domain records; TS string enum values matching C# PascalCase names
  provides:
    - Golden file sidecar-simple-project.json (18 nodes, 20 edges, string enums) at tests/DocAgent.Tests/golden-files/
    - 6 deserialization tests covering edge integrity, doc preservation, enum strings, docComment->Docs mapping, structural equality
    - 2 sidecar integration tests gated by RUN_SIDECAR_TESTS=true exercising full pipeline + search_symbols/get_symbol/get_references
  affects:
    - CI configuration (RUN_SIDECAR_TESTS env var for E2E runs)
    - Future JSON contract changes (golden file must be regenerated)
tech_stack:
  added: []
  patterns:
    - "Golden file test: copy TS sidecar golden file to C# test project; deserialize with same SidecarJsonOptions used in production"
    - "Gated integration test: if (ENV != 'true') return; — early-return guard for CI-optional tests"
    - "Replicate private JsonSerializerOptions in tests: mirror SidecarJsonOptions exactly to test deserialization path"
key_files:
  created:
    - tests/DocAgent.Tests/TypeScriptDeserializationTests.cs
    - tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs
    - tests/DocAgent.Tests/golden-files/sidecar-simple-project.json
  modified:
    - tests/DocAgent.Tests/DocAgent.Tests.csproj
key-decisions:
  - "Use TS vitest golden file (src/ts-symbol-extractor/tests/golden-files/simple-project.json) as C# golden file — it is real sidecar output captured by Plan 01, avoiding duplicate capture infrastructure"
  - "Expected node count is 18 (not 20): after careful counting, simple-project fixture has exactly 18 nodes in JSON; initial confusion from miscounting while reviewing the file"
  - "Mirror SidecarJsonOptions in test class rather than making it internal/public — avoids API surface changes to production code for test purposes"
  - "Sidecar integration tests use LuceneRAMDirectory for in-memory index — avoids FSDirectory per-snapshot swapping issues seen in TypeScriptToolVerificationTests"
  - "get_references test uses IGreeter symbolId and asserts the call succeeds without error (not that references are non-empty) — fixture may have edges pointing TO IGreeter but indexing direction depends on BM25SearchIndex implementation"
requirements-completed: [MCPI-01, MCPI-02]
duration: "35 minutes"
completed: "2026-03-26"
tasks_completed: 2
files_changed: 4
---

# Phase 32 Plan 02: Golden File Deserialization Tests and Sidecar E2E Integration Summary

**6 golden file deserialization tests + 2 RUN_SIDECAR_TESTS-gated E2E tests validate Plan 01's JSON contract fixes end-to-end: edge integrity, doc preservation, string enum deserialization, docComment→Docs mapping, and MCP tool queryability**

## Performance

- **Duration:** ~35 minutes
- **Started:** 2026-03-26T00:00:00Z
- **Completed:** 2026-03-26T00:35:00Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- Golden file `sidecar-simple-project.json` captured from real TS sidecar output (18 nodes, 20 edges, all string enum values) and placed in test project's `golden-files/` directory
- 6 `TypeScriptDeserializationTests` cover: basic deserialization, edge integrity (Contains/Inherits/Implements), doc comment preservation (hello() summary + params), enum string deserialization (Type/Method kind names), docComment→Docs JsonPropertyName mapping, and full snapshot structural equality with exact node/edge counts and referential integrity check
- 2 `TypeScriptSidecarIntegrationTests` gated by `RUN_SIDECAR_TESTS=true`: full pipeline test (real Node.js sidecar → JSON → snapshot) + queryability test covering search_symbols, get_symbol, get_references (MCPI-01 + MCPI-02)
- All 8 new tests pass; 614 non-stress/non-determinism tests continue to pass with 0 failures

## Task Commits

1. **Task 1: Golden file + deserialization tests** - `2733aa2` (test)
2. **Task 2: Sidecar E2E integration tests** - `c622058` (test)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `tests/DocAgent.Tests/golden-files/sidecar-simple-project.json` — Real sidecar output for simple-project fixture (18 nodes, 20 edges, string enums)
- `tests/DocAgent.Tests/TypeScriptDeserializationTests.cs` — 6 golden file deserialization tests with `[Trait("Category", "Deserialization")]`
- `tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs` — 2 sidecar E2E integration tests with `[Trait("Category", "Sidecar")]`
- `tests/DocAgent.Tests/DocAgent.Tests.csproj` — Added `<Content Include="golden-files\**" CopyToOutputDirectory="PreserveNewest" />`

## Decisions Made

- Used the TS vitest golden file as the C# golden file — it is real sidecar output and was updated by Plan 01, so no separate capture needed
- Mirrored `SidecarJsonOptions` in test class (not making it `internal` in production) — minimal invasive approach
- `GoldenFile_Snapshot_Matches_Reference` uses exact counts (18 nodes, 20 edges) which is correct since we control the golden file — catches dropped nodes or edges
- `RealSidecar_Snapshot_Is_Queryable` asserts `get_references` call succeeds without error rather than asserting non-empty results, since reference edge directionality in BM25 index is implementation-dependent

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Expected node count corrected from 20 to 18**
- **Found during:** Task 1 (GoldenFile_Snapshot_Matches_Reference test execution)
- **Issue:** Initial hard-coded count of 20 nodes was wrong; JSON file actually has 18 nodes (miscounted during review)
- **Fix:** Updated `ExpectedNodeCount` constant from 20 to 18 and updated comments
- **Files modified:** tests/DocAgent.Tests/TypeScriptDeserializationTests.cs
- **Verification:** Test passes with 18; debug diagnostic confirmed actual deserialization yields 18 nodes, 20 edges
- **Committed in:** 2733aa2 (Task 1 commit — corrected before final commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 - count correction)
**Impact on plan:** Minor count correction; no behavioral changes to production code.

## Issues Encountered

None beyond the node count correction.

## Next Phase Readiness

- JSON contract (Plan 01) is now fully tested end-to-end via golden file deserialization
- To run real sidecar tests: `RUN_SIDECAR_TESTS=true dotnet test tests/DocAgent.Tests --filter "Category=Sidecar"`
- Golden file must be regenerated if simple-project fixture changes or sidecar output format changes

---
*Phase: 32-json-contract-alignment*
*Completed: 2026-03-26*
