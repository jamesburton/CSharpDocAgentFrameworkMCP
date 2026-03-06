---
phase: 23
slug: dependency-foundation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-06
---

# Phase 23 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| **Config file** | src/Directory.Build.props (shared build props) |
| **Quick run command** | `dotnet restore src/DocAgentFramework.sln && dotnet build src/DocAgentFramework.sln` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~60 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet restore src/DocAgentFramework.sln && dotnet build src/DocAgentFramework.sln`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 23-01-01 | 01 | 1 | PKG-01 | smoke | `dotnet restore src/DocAgentFramework.sln 2>&1 \| grep -c NU1107` (expect 0) | N/A (restore) | pending |
| 23-01-02 | 01 | 1 | PKG-01 | smoke | `dotnet build src/DocAgentFramework.sln` | N/A (build) | pending |
| 23-01-03 | 01 | 1 | PKG-01 | regression | `dotnet test` | Existing suite | pending |
| 23-02-01 | 02 | 1 | PKG-02 | smoke | `dotnet restore src/DocAgentFramework.sln` (no NU190x errors) | N/A (restore) | pending |
| 23-02-02 | 02 | 1 | PKG-02 | regression | `dotnet test` | Existing suite | pending |

*Status: pending / green / red / flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. This phase is build configuration, not feature code. Validation is via restore/build/test commands, not new test files.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Package audit baseline recorded | PKG-02 | Baseline is a documentation artifact | Verify audit baseline snapshot file exists after phase completion |

---

## Validation Sign-Off

- [ ] All tasks have automated verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
