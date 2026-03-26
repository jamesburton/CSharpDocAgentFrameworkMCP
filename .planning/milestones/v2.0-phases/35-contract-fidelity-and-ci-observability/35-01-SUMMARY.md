---
phase: 35-contract-fidelity-and-ci-observability
plan: "01"
subsystem: ts-contract-alignment
tags: [typescript, serialization, contracts, deserialization, test]
dependency_graph:
  requires: []
  provides: [aligned-ts-cs-json-contracts, parameterinfo-isoptional, genericconstraint-typename-fix]
  affects: [ts-symbol-extractor, DocAgent.Core, TypeScriptDeserializationTests]
tech_stack:
  added: []
  patterns: [TDD-red-green, JsonPropertyName-contract-alignment, backward-compatible-record-default]
key_files:
  created:
    - tests/DocAgent.Tests/TypeScriptDeserializationTests.cs (5 new test methods added)
  modified:
    - src/ts-symbol-extractor/src/types.ts
    - src/ts-symbol-extractor/src/extractor.ts
    - src/DocAgent.Core/Symbols.cs
decisions:
  - "IsOptional added at end of ParameterInfo record with default = false for backward compatibility (per project memory: append new fields to end)"
  - "GenericConstraint.name renamed to typeParameterName in TS to match C# JsonPropertyName — TS was the source of the contract bug"
  - "InheritsFrom and Accepts removed from TS SymbolEdgeKind enum — dormant values never emitted; removal eliminates INT-04 latent throw risk"
metrics:
  duration: "42 minutes"
  completed: "2026-03-26T14:36:14Z"
  tasks_completed: 2
  files_modified: 4
  tests_added: 5
  final_test_count: 657
---

# Phase 35 Plan 01: TS/C# Contract Alignment Summary

**One-liner:** Aligned TS/C# JSON contracts by renaming GenericConstraint.name to typeParameterName, adding ParameterInfo.IsOptional with backward-compatible default, and removing dormant InheritsFrom/Accepts from TS SymbolEdgeKind — all three fixes proved by 5 new deserialization tests.

## What Was Built

Closed two v2.0 audit issues:

- **INT-01 (silent data loss):** TS `GenericConstraint` was serializing field as `name` but C# expected `typeParameterName` — renamed TS field and updated extractor object literal.
- **INT-04 (latent deserialization throw):** TS `SymbolEdgeKind` had `InheritsFrom` and `Accepts` values with no C# counterparts — removed both dormant values from the TS enum.
- **ParameterInfo.IsOptional gap:** C# record lacked `isOptional` field that TS was already emitting — added `IsOptional = false` default parameter to the record.

## Commits

| Hash | Message |
|------|---------|
| `1adae8d` | test(35-01): add contract alignment deserialization tests + ParameterInfo.IsOptional |
| `97fcf72` | fix(35-01): align TS/C# JSON contracts for GenericConstraint and SymbolEdgeKind |

## Tasks Completed

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | Add deserialization tests for contract gaps (TDD RED/GREEN) | Done | 1adae8d |
| 2 | Fix TS/C# contract mismatches | Done | 97fcf72 |

## Verification Results

- `npm run build` in `src/ts-symbol-extractor/`: succeeded cleanly
- `dotnet test --filter TypeScriptDeserializationTests`: 11/11 passed (6 original + 5 new)
- Full suite: **657 passed, 0 failed, 2 skipped** (up from 654 baseline)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ParameterInfo.IsOptional added in Task 1 rather than Task 2**
- **Found during:** Task 1 test authoring
- **Issue:** The test `ParameterInfo_IsOptional_True_Deserializes_Correctly` references `result.IsOptional` which requires the C# property to exist. Tests could not compile without it, making the TDD "RED" state a compile error rather than a runtime failure.
- **Fix:** Added `[property: JsonPropertyName("isOptional")] bool IsOptional = false` to `ParameterInfo` record as part of Task 1 commit. Task 2 proceeded with TS-only changes (types.ts, extractor.ts).
- **Files modified:** `src/DocAgent.Core/Symbols.cs`
- **Commit:** 1adae8d

## Self-Check: PASSED

- FOUND: src/ts-symbol-extractor/src/types.ts
- FOUND: src/ts-symbol-extractor/src/extractor.ts
- FOUND: src/DocAgent.Core/Symbols.cs
- FOUND: tests/DocAgent.Tests/TypeScriptDeserializationTests.cs
- FOUND: commit 1adae8d (test: add contract alignment deserialization tests)
- FOUND: commit 97fcf72 (fix: align TS/C# JSON contracts)
