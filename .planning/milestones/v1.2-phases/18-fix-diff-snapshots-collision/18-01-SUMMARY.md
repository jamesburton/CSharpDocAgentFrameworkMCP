---
phase: 18-fix-diff-snapshots-collision
plan: "01"
subsystem: MCP Tools
tags: [bugfix, mcp, tool-naming, solution-tools]
dependency_graph:
  requires: []
  provides: [TOOLS-04-unique-wire-name]
  affects: [SolutionTools, DocTools]
tech_stack:
  added: []
  patterns: [MCP tool wire name via McpServerTool attribute]
key_files:
  modified:
    - src/DocAgent.McpServer/Tools/SolutionTools.cs
decisions:
  - "SolutionTools.DiffSnapshots wire name changed to diff_solution_snapshots; C# method name unchanged"
metrics:
  duration: "~5 minutes"
  tasks_completed: 1
  files_changed: 1
  completed_date: "2026-03-02"
---

# Phase 18 Plan 01: Fix diff_snapshots MCP Tool Name Collision Summary

**One-liner:** Renamed SolutionTools MCP wire name from `diff_snapshots` to `diff_solution_snapshots` to resolve DEFECT-01 duplicate tool name collision with DocTools.

## What Was Built

Single attribute change in `SolutionTools.cs`: the `[McpServerTool(Name = ...)]` attribute on the `DiffSnapshots` method was updated from `"diff_snapshots"` to `"diff_solution_snapshots"`, and the description was updated to clarify solution-level scope.

## Tasks Completed

| Task | Description | Commit | Files |
|------|-------------|--------|-------|
| 1 | Rename SolutionTools diff_snapshots wire name to diff_solution_snapshots | 6f47e1a | src/DocAgent.McpServer/Tools/SolutionTools.cs |

## Verification

- `grep` confirms SolutionTools.cs has `diff_solution_snapshots`
- `grep` confirms DocTools.cs still has `diff_snapshots` (unchanged)
- `dotnet build` — clean build, no errors
- Tests call `DiffSnapshots(...)` as C# method; no test references the MCP wire name string directly

## Deviations from Plan

None — plan executed exactly as written.

## Self-Check: PASSED

- File exists: `src/DocAgent.McpServer/Tools/SolutionTools.cs` — FOUND
- Commit exists: `6f47e1a` — FOUND
- Wire name `diff_solution_snapshots` on SolutionTools — CONFIRMED
- Wire name `diff_snapshots` on DocTools unchanged — CONFIRMED
