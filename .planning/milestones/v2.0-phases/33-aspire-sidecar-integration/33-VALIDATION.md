---
phase: 33
slug: aspire-sidecar-integration
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-26
---

# Phase 33 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| **Config file** | none (standard xUnit discovery) |
| **Quick run command** | `dotnet test --filter "FullyQualifiedName~NodeAvailability"` |
| **Full suite command** | `dotnet test src/DocAgentFramework.sln` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName~NodeAvailability"`
- **After every plan wave:** Run `dotnet test src/DocAgentFramework.sln`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | Status |
|---------|------|------|-------------|-----------|-------------------|--------|
| 33-01-01 | 01 | 1 | SIDE-04 | build + unit | `dotnet build src/DocAgent.AppHost && dotnet test --filter "FullyQualifiedName~NodeAvailability"` | pending |
| 33-01-02 | 01 | 1 | SIDE-04 | unit | `dotnet test --filter "FullyQualifiedName~NodeAvailability"` | pending |

*Status: pending / green / red / flaky*

---

## Wave 0 Requirements

Wave 0 is not needed for this phase. Each task creates and verifies its own tests inline. Tasks create the test files themselves and run them as verification.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Aspire dashboard shows sidecar resource | SIDE-04 | Dashboard is visual | Run `dotnet run --project src/DocAgent.AppHost`, open dashboard, verify sidecar appears |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify commands
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 not required — tasks create their own tests
- [x] No watch-mode flags
- [x] Feedback latency < 30s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
