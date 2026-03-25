---
phase: 32
slug: json-contract-alignment
status: draft
nyquist_compliant: false
wave_0_complete: false
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

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 32-01-01 | 01 | 1 | SIDE-03 | unit (golden file) | `dotnet test --filter "Category=Deserialization"` | ❌ W0 | ⬜ pending |
| 32-01-02 | 01 | 1 | EXTR-04 | unit (golden file) | `dotnet test --filter "Category=Deserialization"` | ❌ W0 | ⬜ pending |
| 32-01-03 | 01 | 1 | EXTR-06 | unit (golden file) | `dotnet test --filter "Category=Deserialization"` | ❌ W0 | ⬜ pending |
| 32-02-01 | 02 | 2 | MCPI-01 | integration | `dotnet test --filter "Category=E2E"` | ❌ W0 | ⬜ pending |
| 32-02-02 | 02 | 2 | MCPI-02 | integration | `dotnet test --filter "Category=E2E"` | ❌ W0 | ⬜ pending |
| 32-02-03 | 02 | 2 | MCPI-02 | e2e (sidecar) | `RUN_SIDECAR_TESTS=true dotnet test --filter "Category=Sidecar"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/DocAgent.Tests/TypeScriptDeserializationTests.cs` — stubs for SIDE-03, EXTR-04, EXTR-06 using golden file
- [ ] `tests/DocAgent.Tests/TypeScriptContractE2ETests.cs` — stubs for MCPI-01, MCPI-02 with full MCP tool verification
- [ ] `tests/DocAgent.Tests/TypeScriptSidecarIntegrationTests.cs` — stubs for MCPI-02 sidecar path (gated by RUN_SIDECAR_TESTS)
- [ ] Golden file captured from real sidecar output against test fixtures

---

## Manual-Only Verifications

*All phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
