---
phase: 28
slug: sidecar-scaffold-and-ipc-protocol
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-08
---

# Phase 28 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework (C#)** | xUnit + FluentAssertions (existing) |
| **Framework (Node.js)** | vitest ^3.x (new) |
| **Config file (C#)** | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| **Config file (Node.js)** | `src/ts-symbol-extractor/vitest.config.ts` (Wave 0) |
| **Quick run command (C#)** | `dotnet test --filter "FullyQualifiedName~TypeScriptIngestion"` |
| **Quick run command (Node.js)** | `cd src/ts-symbol-extractor && npm test` |
| **Full suite command** | `dotnet test && cd src/ts-symbol-extractor && npm test` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName~TypeScriptIngestion"` + `cd src/ts-symbol-extractor && npm test`
- **After every plan wave:** Run `dotnet test && cd src/ts-symbol-extractor && npm test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 28-01-01 | 01 | 1 | SIDE-01 | smoke | `cd src/ts-symbol-extractor && npm run build && npm test` | ❌ W0 | ⬜ pending |
| 28-01-02 | 01 | 1 | SIDE-02 | unit | `cd src/ts-symbol-extractor && npx vitest run tests/stub-extractor.test.ts` | ❌ W0 | ⬜ pending |
| 28-02-01 | 02 | 1 | SIDE-03 | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptIngestionServiceTests"` | ❌ W0 | ⬜ pending |
| 28-02-02 | 02 | 1 | SIDE-04 | unit | `dotnet test --filter "FullyQualifiedName~NodeAvailabilityValidator"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `src/ts-symbol-extractor/` — entire sidecar project (package.json, tsconfig.json, vitest.config.ts, build.mjs)
- [ ] `src/ts-symbol-extractor/tests/stub-extractor.test.ts` — covers SIDE-01, SIDE-02
- [ ] `tests/DocAgent.Tests/TypeScriptIngestionServiceTests.cs` — covers SIDE-03
- [ ] `tests/DocAgent.Tests/NodeAvailabilityValidatorTests.cs` — covers SIDE-04

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Aspire AppHost starts sidecar | SIDE-04 | Requires Aspire runtime + Node.js installed | Run `dotnet run --project src/DocAgent.AppHost`, verify sidecar resource appears in dashboard |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
