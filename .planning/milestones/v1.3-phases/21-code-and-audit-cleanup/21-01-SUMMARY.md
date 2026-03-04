---
phase: 21-code-and-audit-cleanup
plan: 01
status: complete
started: 2026-03-03
completed: 2026-03-03
requirements_completed: [QUAL-01, QUAL-02, QUAL-03]
---

# 21-01 Summary: Remove stale comments, fix audit issues, wire benchmark and OTel

## What Was Done

**Task 1** (commit 8e9f696):
- Removed stale `TODO: replace with BM25` comment from `InMemorySearchIndex.cs`
- Replaced stale `stub — MCPS-03` comment in `KnowledgeQueryService.cs` with clean section header
- Added `requirements_completed: [TOOLS-05]` to `16-01-SUMMARY.md` frontmatter

**Task 2** (commit 1dfff47):
- Changed `SolutionIngestionBenchmarks._service` field to `ISolutionIngestionService` interface type
- `GlobalSetup` now wraps `SolutionIngestionService` in `IncrementalSolutionIngestionService` decorator
- Added `metrics.AddMeter("DocAgent.Ingestion")` to `Program.cs` for OTel counter collection

## Decisions

- Single plan with 2 tasks sufficient for all 5 success criteria (all surgical fixes)

## Verification

All 5 success criteria confirmed via grep checks. Build passes. 309 tests pass (21 pre-existing environment-dependent failures unrelated to these changes).
