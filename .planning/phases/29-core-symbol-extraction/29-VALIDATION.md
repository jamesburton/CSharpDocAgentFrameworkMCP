---
phase: 29
slug: core-symbol-extraction
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-26
note: "Retroactive validation — phase completed 2026-03-24 and verified via 29-VERIFICATION.md (score: 8/8)"
---

# Phase 29 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> **Retroactive attestation:** Phase 29 completed 2026-03-24. All tasks are complete and verified.
> Evidence: `29-VERIFICATION.md` (score: 8/8 requirements verified, re-verification passed).

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework (Node.js)** | vitest ^3.x |
| **Config file (Node.js)** | `src/ts-symbol-extractor/vitest.config.ts` |
| **Quick run command** | `cd src/ts-symbol-extractor && npm test` |
| **Full suite command** | `dotnet test && cd src/ts-symbol-extractor && npm test` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `cd src/ts-symbol-extractor && npm test`
- **After every plan wave:** Run `dotnet test && cd src/ts-symbol-extractor && npm test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Requirement | Test Type | Automated Command | Status |
|---------|------|-------------|-----------|-------------------|--------|
| 29-01-01 | 01 | EXTR-02 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-01-02 | 01 | EXTR-03 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-01-03 | 01 | EXTR-07 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-01-04 | 01 | EXTR-08 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-02-01 | 02 | EXTR-01 | unit (golden file) | `cd src/ts-symbol-extractor && npx vitest run` | complete |
| 29-02-02 | 02 | EXTR-04 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-02-03 | 02 | EXTR-05 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-02-04 | 02 | EXTR-06 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/extractor.test.ts` | complete |
| 29-03-01 | 03 | EXTR-01 | unit (gap closure) | `cd src/ts-symbol-extractor && npx vitest run` | complete |

*Status: complete (all tasks verified in 29-VERIFICATION.md)*

---

## Wave 0 Requirements

Wave 0 was fulfilled during plan 29-01 execution. The full sidecar project (`src/ts-symbol-extractor/`) including `vitest.config.ts`, `tests/extractor.test.ts`, and `tests/ipc.test.ts` were created as part of plan 29-01 and verified in plan 29-01's task commits.

---

## Manual-Only Verifications

*All phase behaviors have automated verification. See 29-VERIFICATION.md.*

---

## Validation Sign-Off

- [x] All tasks have automated verify commands
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 fulfilled during plan 29-01 execution
- [x] No watch-mode flags
- [x] Feedback latency < 15s
- [x] `nyquist_compliant: true` set in frontmatter

**Evidence:** `29-VERIFICATION.md` — score 8/8, re-verification passed (gap closure in 29-03 fixed EXTR-01 enum member extraction bug)
**Approval:** retroactive — phase complete
