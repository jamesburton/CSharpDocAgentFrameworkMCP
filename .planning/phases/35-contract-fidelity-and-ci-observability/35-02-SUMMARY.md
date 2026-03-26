---
phase: 35-contract-fidelity-and-ci-observability
plan: "02"
subsystem: testing-observability
tags: [ci, xunit, aspire, testing, observability]
dependency_graph:
  requires: []
  provides: [honest-ci-test-output, aspire-dependency-graph, testing-docs]
  affects: [TypeScriptSidecarIntegrationTests, DocAgent.AppHost, docs/Testing.md]
tech_stack:
  added: []
  patterns: [xunit-skip-attribute, aspire-withreference]
key_files:
  created: []
  modified:
    - tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs
    - src/DocAgent.AppHost/Program.cs
    - docs/Testing.md
decisions:
  - "Static [Fact(Skip=...)] chosen over conditional skip logic — honest CI output, no env var magic"
  - "WithReference(sidecar) used for Aspire dependency wiring — creates dashboard link without WaitFor"
metrics:
  duration_minutes: 45
  tasks_completed: 2
  files_modified: 3
  completed_date: "2026-03-26"
---

# Phase 35 Plan 02: CI Observability and Aspire Dependency Wiring Summary

Static skip attributes on sidecar E2E tests and Aspire WithReference dependency link close INT-02 and INT-03 from the v2.0 milestone audit.

## What Was Built

### Task 1: Replace early-return guards with xUnit Skip attributes

Replaced the early-return guard pattern (`if (RUN_SIDECAR_TESTS != "true") return;`) on both sidecar E2E test methods with static `[Fact(Skip=...)]` attributes. This means both tests now show as **Skipped** (not silently Passed) in standard `dotnet test` output, giving honest CI visibility.

**Before (INT-02 bug):**
```csharp
[Fact]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    if (Environment.GetEnvironmentVariable("RUN_SIDECAR_TESTS") != "true")
        return; // showed as "Passed" — no assertions ran
```

**After:**
```csharp
[Fact(Skip = "Requires Node.js and compiled sidecar. Set RUN_SIDECAR_TESTS=true and run: dotnet test --filter 'FullyQualifiedName~TypeScriptSidecarIntegration'")]
public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
{
    // no early return — all assertions run when Skip removed
```

**Verification:** `dotnet test --filter "FullyQualifiedName~TypeScriptSidecarIntegration"` shows:
```
Skipped DocAgent.Tests.TypeScriptSidecarIntegrationTests.RealSidecar_SimpleProject_Produces_Valid_Snapshot
Skipped DocAgent.Tests.TypeScriptSidecarIntegrationTests.RealSidecar_Snapshot_Is_Queryable
Skipped! - Failed: 0, Passed: 0, Skipped: 2, Total: 2
```

### Task 2: Aspire dependency wiring and benchmark documentation

**Aspire wiring (INT-03):** Added `.WithReference(sidecar)` to the mcpServer builder chain in `src/DocAgent.AppHost/Program.cs`. This creates a dependency link in the Aspire dashboard resource graph without imposing startup ordering. The locked decision (no WaitFor) is preserved.

The lambda approach (`ctx => sidecarDir`) was tried first but does not match any available `WithEnvironment` overload. `WithReference(sidecar)` compiled cleanly and is the preferred approach.

**Documentation:** Added two new sections to `docs/Testing.md`:
- `### TypeScript Sidecar Integration Tests` — prerequisites, skip behavior explanation, manual run instructions
- `### TypeScriptIngestionBenchmarks` — prerequisites, run commands, CI integration guidance

## Verification Results

```
Full test suite: Passed! - Failed: 0, Passed: 657, Skipped: 2, Total: 659
AppHost build:   Build succeeded. 0 Warning(s), 0 Error(s)
Sidecar filter:  Skipped! - Failed: 0, Passed: 0, Skipped: 2, Total: 2
```

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written.

The plan listed three fallback approaches for Aspire dependency wiring (WithReference, env lambda, code comment). The first approach (`WithReference(sidecar)`) compiled cleanly on first attempt, so no fallback was needed. The env lambda approach was briefly attempted and failed compilation (CS1660: cannot convert lambda to string), confirming the fallback ordering was correct.

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Static `[Fact(Skip=...)]` for sidecar tests | Honest CI output — tests show as Skipped not silently Passed; skip message explains prerequisites |
| `.WithReference(sidecar)` for Aspire dependency | Compiles cleanly, creates dashboard dependency link, no startup ordering imposed |

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| TypeScriptSidecarIntegrationTests.cs exists | FOUND |
| Program.cs exists | FOUND |
| docs/Testing.md exists | FOUND |
| Task 1 commit 067c285 exists | FOUND |
| Task 2 commit ecbbb5a exists | FOUND |
| `[Fact(Skip` appears 3 times in test file | FOUND |
| `WithReference` appears in Program.cs | FOUND |
| `TypeScriptIngestionBenchmarks` appears in Testing.md | FOUND (x2) |
