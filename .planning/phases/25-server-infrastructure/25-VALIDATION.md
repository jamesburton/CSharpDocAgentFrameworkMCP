---
phase: 25
slug: server-infrastructure
status: approved
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
validated: 2026-03-08
---

# Phase 25 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + FluentAssertions 6.12.1 |
| **Config file** | tests/DocAgent.Tests/DocAgent.Tests.csproj |
| **Quick run command** | `dotnet build src/DocAgentFramework.sln` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~60 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build src/DocAgentFramework.sln`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 60 seconds

---

## Per-Task Verification Map

### Plan 01: Startup Validation (OPS-02)

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 25-01-01 | 01 | 1 | OPS-02 | unit | `dotnet test --filter "FullyQualifiedName~StartupValidatorTests"` | ✅ StartupValidatorTests.cs | ✅ green |
| 25-01-02 | 01 | 1 | OPS-02 | regression | `dotnet test` | Full suite | ✅ green |

### Plan 02: Rate Limiting (OPS-03)

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 25-02-01 | 02 | 1 | OPS-03 | unit | `dotnet test --filter "FullyQualifiedName~RateLimitTests"` | ✅ RateLimitTests.cs | ✅ green |
| 25-02-02 | 02 | 1 | OPS-03 | regression | `dotnet test` | Full suite | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

**Test counts:**
- StartupValidatorTests: 6 tests (valid config, empty AllowedPaths warning, null/empty ArtifactsDir error, non-writable ArtifactsDir error, env var override)
- RateLimitTests: 8 tests (within-limit, exceeded-limit, separate buckets, disabled mode, tool categorization)

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements.

---

## Manual-Only Verifications

All phase behaviors have automated verification.

---

## Validation Sign-Off

- [x] All tasks have automated verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 60s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** approved 2026-03-08

## Validation Audit 2026-03-08

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
