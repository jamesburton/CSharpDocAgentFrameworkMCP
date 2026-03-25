---
phase: 32
slug: json-contract-alignment
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-25
---

# Phase 32 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.x (existing) |
| **Config file** | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| **Quick run command** | `dotnet test --filter "Category=Deserialization" --no-build` |
| **Full suite command** | `dotnet test src/DocAgentFramework.sln` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "Category=Deserialization" --no-build`
- **After every plan wave:** Run `dotnet test src/DocAgentFramework.sln`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | Status |
|---------|------|------|-------------|-----------|-------------------|--------|
| 32-01-01 | 01 | 1 | SIDE-03 | unit (TS) | `cd src/ts-symbol-extractor && npx tsc --noEmit && npx vitest run` | pending |
| 32-01-02 | 01 | 1 | EXTR-04, EXTR-06 | build | `dotnet build src/DocAgentFramework.sln && dotnet test src/DocAgentFramework.sln --filter "FullyQualifiedName~TypeScript" --no-build` | pending |
| 32-02-01 | 02 | 2 | MCPI-01 | unit (golden file) | `dotnet test tests/DocAgent.Tests --filter "Category=Deserialization" --no-build` | pending |
| 32-02-02 | 02 | 2 | MCPI-02 | integration | `dotnet build tests/DocAgent.Tests && dotnet test tests/DocAgent.Tests --filter "Category=Sidecar" --no-build` | pending |

*Status: pending / green / red / flaky*

---

## Wave 0 Requirements

Wave 0 is not needed for this phase. Each task creates and verifies its own tests inline. Plan 01 tasks are pure implementation (no test files). Plan 02 tasks create the test files themselves and run them as verification.

---

## Manual-Only Verifications

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 not required — tasks create their own tests
- [x] No watch-mode flags
- [x] Feedback latency < 30s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
