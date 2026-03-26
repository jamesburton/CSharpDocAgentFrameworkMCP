---
phase: 31
slug: verification-and-hardening
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-26
note: "Retroactive validation — phase completed 2026-03-25 and verified via 31-VERIFICATION.md (score: 10/10)"
---

# Phase 31 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> **Retroactive attestation:** Phase 31 completed 2026-03-25. All tasks are complete and verified.
> Evidence: `31-VERIFICATION.md` (score: 10/10 must-haves verified, re-verification passed).

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework (C#)** | xUnit + FluentAssertions |
| **Framework (Node.js)** | vitest ^3.x |
| **Config file (C#)** | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| **Config file (Node.js)** | `src/ts-symbol-extractor/vitest.config.ts` |
| **Quick run command** | `dotnet test --filter "FullyQualifiedName~TypeScript"` |
| **Full suite command** | `dotnet test && cd src/ts-symbol-extractor && npm test` |
| **Estimated runtime** | ~30 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName~TypeScript"`
- **After every plan wave:** Run `dotnet test && cd src/ts-symbol-extractor && npm test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 30 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Requirement | Test Type | Automated Command | Status |
|---------|------|-------------|-----------|-------------------|--------|
| 31-01-01 | 01 | VERF-01 | unit (determinism) | `dotnet test --filter "FullyQualifiedName~TypeScriptDeterminism"` | complete |
| 31-01-02 | 01 | VERF-02 | integration (12 tools) | `dotnet test --filter "FullyQualifiedName~TypeScriptDeterminism"` | complete |
| 31-01-03 | 01 | VERF-04 | benchmark | `dotnet test --filter "FullyQualifiedName~TypeScriptStress"` | complete |
| 31-02-01 | 02 | VERF-03 | unit (security) | `dotnet test --filter "FullyQualifiedName~TypeScriptRobustness"` | complete |
| 31-02-02 | 02 | VERF-04 | stress (110 files) | `dotnet test --filter "FullyQualifiedName~TypeScriptStress"` | complete |
| 31-03-01 | 03 | VERF-03 | unit (relative paths) | `dotnet test --filter "FullyQualifiedName~TypeScriptRobustness"` | complete |
| 31-04-01 | 04 | VERF-03 | unit (audit logging) | `dotnet test --filter "FullyQualifiedName~TypeScriptRobustness"` | complete |

*Status: complete (all tasks verified in 31-VERIFICATION.md)*

---

## Wave 0 Requirements

Wave 0 was not separately required for this phase. All test infrastructure (stress, determinism, robustness test files, benchmark project) was created inline during plan execution. Plans 31-03 and 31-04 performed gap closure — fixing the absolute path leak and wiring audit logging.

---

## Manual-Only Verifications

*All phase behaviors have automated verification. See 31-VERIFICATION.md.*

---

## Validation Sign-Off

- [x] All tasks have automated verify commands
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 not required — tasks created their own tests
- [x] No watch-mode flags
- [x] Feedback latency < 30s
- [x] `nyquist_compliant: true` set in frontmatter

**Evidence:** `31-VERIFICATION.md` — score 10/10, re-verification passed (gap closure in 31-03 fixed absolute path leak; 31-04 wired AuditLogger and added Architecture.md TypeScript section)
**Approval:** retroactive — phase complete
