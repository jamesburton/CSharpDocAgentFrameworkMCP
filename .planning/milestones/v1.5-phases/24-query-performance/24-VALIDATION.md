---
phase: 24
slug: query-performance
status: approved
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
validated: 2026-03-08
---

# Phase 24 — Validation Strategy

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

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 24-01-01 | 01 | 1 | PERF-01, PERF-02, PERF-03 | smoke | `dotnet build src/DocAgentFramework.sln` | N/A (build) | ✅ green |
| 24-01-02 | 01 | 1 | PERF-01, PERF-02, PERF-03 | regression | `dotnet test` | KnowledgeQueryServiceTests.cs + full suite (335 tests) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

**Coverage note:** SnapshotLookup is a private nested class in KnowledgeQueryService — not directly testable. All 335 existing tests exercise the query path through GetSymbolAsync, GetReferencesAsync, and SearchAsync, implicitly validating O(1) dictionary lookups produce identical results to the replaced linear scans. Determinism tests (DeterminismTests.cs) verify output identity.

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. SnapshotLookup is an internal optimization; correctness is validated by the existing test suite producing identical results.

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
