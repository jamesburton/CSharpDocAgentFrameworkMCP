---
phase: 35
slug: contract-fidelity-and-ci-observability
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-26
---

# Phase 35 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + FluentAssertions (C#), vitest (TypeScript) |
| **Config file** | `src/DocAgentFramework.sln` (C#), `src/ts-symbol-extractor/vitest.config.ts` (TS) |
| **Quick run command** | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30 seconds (quick), ~90 seconds (full) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 35-01-01 | 01 | 1 | SIDE-03, EXTR-01 | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | ✅ | ⬜ pending |
| 35-01-02 | 01 | 1 | EXTR-01 | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | ✅ | ⬜ pending |
| 35-01-03 | 01 | 1 | EXTR-04 | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptDeserialization"` | ✅ | ⬜ pending |
| 35-02-01 | 02 | 1 | SIDE-03 | unit | `dotnet test --filter "FullyQualifiedName~TypeScriptSidecar"` | ✅ | ⬜ pending |
| 35-02-02 | 02 | 1 | SIDE-04 | build | `dotnet build src/DocAgent.AppHost` | ✅ | ⬜ pending |
| 35-02-03 | 02 | 1 | — | docs | manual review | — | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. TypeScriptDeserializationTests and TypeScriptSidecarIntegrationTests already exist.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Aspire dashboard shows ts-sidecar → docagent-mcp dependency | SIDE-04 | Requires running Aspire dashboard | Run `dotnet run --project src/DocAgent.AppHost`, check dashboard |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
