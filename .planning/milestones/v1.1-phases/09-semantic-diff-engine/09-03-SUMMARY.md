---
phase: 09-semantic-diff-engine
plan: 03
subsystem: semantic-diff-tests
tags: [diff-engine, tests, determinism, MessagePack, FluentAssertions]
dependency_graph:
  requires: [09-01, 09-02]
  provides: [SemanticDiff test suite]
  affects: []
tech_stack:
  added: []
  patterns: [xUnit, FluentAssertions, MessagePack-roundtrip, in-memory snapshot builders]
key_files:
  created:
    - tests/DocAgent.Tests/SemanticDiff/DiffTestHelpers.cs
    - tests/DocAgent.Tests/SemanticDiff/SymbolGraphDifferTests.cs
    - tests/DocAgent.Tests/SemanticDiff/SignatureChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/NullabilityChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/ConstraintChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/AccessibilityChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/DependencyChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/DocCommentChangeTests.cs
    - tests/DocAgent.Tests/SemanticDiff/DiffDeterminismTests.cs
  modified: []
decisions:
  - "DiffTestHelpers uses BuildSnapshot overloads with optional projectName for incompatible-snapshot tests"
  - "NullabilityChangeTests verifies the OldAnnotation/NewAnnotation fields on NullabilityChangeDetail"
  - "DiffDeterminismTests uses ContractlessStandardResolver for MessagePack roundtrip matching existing serialization tests"
metrics:
  duration: "~10 minutes"
  completed: "2026-02-28"
  tasks_completed: 2
  files_changed: 9
---

# Phase 9 Plan 03: Semantic Diff Test Suite Summary

**One-liner:** 24 xUnit tests across 8 test classes covering all six SymbolGraphDiffer change categories (Signature, Nullability, Constraint, Accessibility, Dependency, DocComment) plus determinism verified via MessagePack byte-identical roundtrip.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create DiffTestHelpers and core SymbolGraphDifferTests | dde5820 | DiffTestHelpers.cs, SymbolGraphDifferTests.cs |
| 2 | Create per-category change tests and determinism tests | 8341fab | 7 test files |

## What Was Built

### DiffTestHelpers.cs

Shared static builder class providing:
- `BuildSnapshot(params SymbolNode[])` — builds an in-memory `SymbolGraphSnapshot` for "TestProject"
- `BuildSnapshot(string projectName, SymbolNode[], SymbolEdge[])` — for incompatible-snapshot tests
- `BuildMethod(id, access, returnType, parameters, constraints, docs)` — builds a `SymbolKind.Method` node
- `BuildType(id, access, constraints, docs)` — builds a `SymbolKind.Type` node
- `BuildDoc(summary)` — creates a minimal `DocComment` with a summary
- `Param(name, typeName, defaultValue)` — creates `ParameterInfo`

### SymbolGraphDifferTests.cs (7 tests)

Core algorithm coverage:
- `Diff_throws_for_different_project_names` — ArgumentException on incompatible snapshots
- `Diff_detects_added_symbol` — ChangeType.Added for new symbol
- `Diff_detects_removed_symbol` — ChangeType.Removed for missing symbol
- `Diff_empty_snapshots_produces_empty_diff` — zero changes, all summary counts zero
- `Diff_identical_snapshots_produces_no_changes` — same content yields empty diff
- `Diff_summary_counts_match_changes` — Summary fields equal actual Changes counts
- `Diff_changes_sorted_by_symbol_id_then_category` — deterministic ordering verified

### Per-Category Test Classes

| Class | Tests | Categories Covered |
|-------|-------|--------------------|
| SignatureChangeTests | 3 | Return type, parameter added, parameter type change |
| NullabilityChangeTests | 2 | Return type ?, parameter ? |
| ConstraintChangeTests | 2 | where T : class added/removed |
| AccessibilityChangeTests | 3 | Public→Internal (Breaking), Internal→Public (NonBreaking), internal non-breaking |
| DependencyChangeTests | 2 | New References edge, removed edge |
| DocCommentChangeTests | 2 | Doc added (null→DocComment), doc changed (Informational) |
| DiffDeterminismTests | 3 | Repeated calls identical, MessagePack roundtrip byte-identical, order consistent |

### Test Results

All 24 tests green. Zero regressions in SemanticDiff namespace.

## Deviations from Plan

None — plan executed exactly as written. SymbolGraphDiffer.cs was already present from 09-02 execution so no blocking issue occurred.

## Self-Check: PASSED

Files exist:
- tests/DocAgent.Tests/SemanticDiff/DiffTestHelpers.cs — FOUND
- tests/DocAgent.Tests/SemanticDiff/SymbolGraphDifferTests.cs — FOUND
- tests/DocAgent.Tests/SemanticDiff/DiffDeterminismTests.cs — FOUND

Commits exist:
- dde5820 — FOUND
- 8341fab — FOUND
