---
phase: 11-change-intelligence-review
plan: "02"
subsystem: mcp-tools
tags: [mcp-tools, change-intelligence, tron-serializer, snapshot-store, csharp]
dependency_graph:
  requires: [11-01-SUMMARY.md, 09-02-SUMMARY.md]
  provides: [ChangeTools, TronSerializer.SerializeChangeReview, TronSerializer.SerializeBreakingChanges]
  affects: [11-03-PLAN.md]
tech_stack:
  added: []
  patterns: [mcp-server-tool-type, snapshot-store-real-io, tron-serializer, format-triple-factory]
key_files:
  created:
    - src/DocAgent.McpServer/Tools/ChangeTools.cs
    - tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs
  modified:
    - src/DocAgent.McpServer/Serialization/TronSerializer.cs
decisions:
  - "ExplainChangeDetail is a private sealed record inside ChangeTools — avoids leaking DTOs to public namespace"
  - "ImpactScope in review_changes uses all edges where To==symbolId; explain_change scopes to Calls edges only"
  - "Tron format for explain_change uses inline Utf8JsonWriter (not TronSerializer) since it serializes a different schema shape"
metrics:
  duration_seconds: 420
  completed_date: "2026-02-28T22:17:00Z"
  tasks_completed: 2
  files_created: 2
  files_modified: 1
---

# Phase 11 Plan 02: MCP Change Intelligence Tools Summary

**One-liner:** Three MCP tools (review_changes, find_breaking_changes, explain_change) wired to ChangeReviewer and SymbolGraphDiffer, with json/markdown/tron output, prompt injection scanning, and 10 passing unit tests.

## What Was Built

**ChangeTools.cs** — new `[McpServerToolType]` class with three MCP tools:

1. **review_changes** — loads two snapshots, diffs them via `SymbolGraphDiffer.Diff`, analyzes with `ChangeReviewer.Analyze`, populates ImpactScope from snapshotB edges, scans for prompt injection, returns structured findings grouped by severity with unusual findings section.

2. **find_breaking_changes** — same load/diff pattern, filters to `ChangeSeverity.Breaking` only, returns CI-minimal shape: `{ beforeVersion, afterVersion, breakingCount, breakingChanges: [...] }`.

3. **explain_change** — loads/diffs, filters to a single symbolId, builds per-change detail records with `whyItMatters` text, callers from Calls edges in snapshotB, and remediation text.

All three tools support json/markdown/tron output via `FormatResponse` triple-factory. Error cases (snapshot missing, mismatched projects, symbol not found) return structured JSON errors, not exceptions.

**TronSerializer.cs** additions:
- `SerializeChangeReview(ChangeReviewReport)` — schema: `[symbolId, displayName, severity, category, description, remediation]`
- `SerializeBreakingChanges(string, string, IReadOnlyList<SymbolChange>)` — schema: `[symbolId, displayName, description]` with beforeVersion/afterVersion fields

**ChangeToolTests.cs** — 10 unit tests using real `SnapshotStore` with IDisposable temp-directory cleanup.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Create ChangeTools MCP class and TronSerializer additions | b99c2bd | ChangeTools.cs, TronSerializer.cs |
| 2 | Create ChangeTools unit tests | fab05ab | ChangeToolTests.cs |

## Verification

- `dotnet build src/DocAgent.McpServer` — 0 errors, 0 warnings
- `dotnet test --filter "FullyQualifiedName~ChangeToolTests"` — 10/10 passed
- `dotnet test` — 241/241 passed (all existing tests green)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Added IDisposable cleanup for temp directory**
- **Found during:** Task 2 implementation
- **Issue:** Test class creates a unique temp directory per test run; without cleanup these accumulate indefinitely
- **Fix:** Added IDisposable with `Directory.Delete` in `Dispose()` method
- **Files modified:** tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs
- **Commit:** fab05ab

**2. [Rule 3 - Blocking issue] ExplainChange tron format used inline writer, not TronSerializer**
- **Found during:** Task 1 implementation
- **Issue:** Plan says `explain_change` tron format via "new TronSerializer method" but the explain_change shape (symbolId + multiple changes per row) doesn't fit the flat schema pattern of existing TronSerializer methods
- **Fix:** Implemented as inline `SerializeExplainChangeTron` private method in ChangeTools.cs using Utf8JsonWriter directly; matches tron format contract (has `$schema` array) without polluting TronSerializer with private DTOs
- **Files modified:** src/DocAgent.McpServer/Tools/ChangeTools.cs
- **Commit:** b99c2bd

## Self-Check: PASSED

Files exist:
- FOUND: src/DocAgent.McpServer/Tools/ChangeTools.cs
- FOUND: src/DocAgent.McpServer/Serialization/TronSerializer.cs (modified)
- FOUND: tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs

Commits exist:
- FOUND: b99c2bd
- FOUND: fab05ab
