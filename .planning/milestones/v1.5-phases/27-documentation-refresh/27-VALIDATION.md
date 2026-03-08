---
phase: 27
slug: documentation-refresh
status: approved
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
validated: 2026-03-08
---

# Phase 27 — Validation Strategy

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
| 27-01-01 | 01 | 1 | OPS-01 | smoke | `dotnet build src/DocAgentFramework.sln` (build still passes) | N/A (docs) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. This phase is documentation only — no new code to test.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| CLAUDE.md lists all 14 MCP tools | OPS-01 | Documentation content, not code behavior | Count `[McpServerTool]` attributes in source (expect 14), count tool entries in CLAUDE.md (expect 14), compare parameter signatures |
| Parameter signatures match source | OPS-01 | Cross-reference exercise | For each tool entry in CLAUDE.md, verify parameter names/types/defaults match the `[McpServerTool]`-decorated method in source |
| projectFilter documented | OPS-01 | Documentation completeness | Verify search_symbols and get_doc_coverage entries mention `project` parameter |

**Note:** Phase 27 SUMMARY.md includes a verification checklist confirming all 14 tools documented, parameter signatures verified against source, and projectFilter documented. Manual verification was performed during execution.

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
