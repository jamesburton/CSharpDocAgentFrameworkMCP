---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: TypeScript Language Support
status: Between milestones — ad-hoc feature work
stopped_at: Completed 31-04-PLAN.md
last_updated: "2026-03-25T20:27:46.814Z"
last_activity: 2026-03-25 — Completed 31-04 (TypeScript audit logging + Architecture.md sidecar docs)
progress:
  total_phases: 4
  completed_phases: 4
  total_plans: 12
  completed_plans: 12
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-03-08)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET or TypeScript codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** MCP Setup & Installation System (CLI for onboarding users and projects)

## Current Position

Milestone v2.0 (TypeScript Language Support): SHIPPED
Post-milestone work: v2.1.0 shipped (large solution ingestion optimisations) + MCP setup CLI

Status: Between milestones — ad-hoc feature work
Last activity: 2026-03-25 — Completed 31-04 (TypeScript audit logging + Architecture.md sidecar docs)

Progress (v2.0): [▓▓▓▓▓▓▓▓▓▓] 100%

## Post-v2.0 Shipped Work

### v2.1.0 — Large Solution Ingestion Optimisations (shipped ~2026-03-13)
- `ProjectSnapshotSummary` lightweight record replacing full snapshots in `SolutionSnapshot`
- Raised ingestion timeout 300→1800s; test file exclusion (`ExcludeTestFiles`)
- O(1) `IsKnownNode` fix; span-based fingerprints; `ArrayPool<byte>` usage
- Per-project checkpoint saves; aggressive GC between projects
- 381→490 tests at time of last run

### MCP Setup & Installation System (11 commits, latest 1f9a13d)
- `ProjectConfig` / `UserConfig` types with JSON schemas
- `CliRunner` routing with command stubs
- `UpdateCommand` — full ingestion with JSON summary and `--quiet` flag
- `InstallCommand` with `SkillContent` and tests
- `InitCommand` — full project initialisation logic and tests
- `HooksCommand` — sentinel-based git hook management
- `AgentDetector` — probes installed AI agent tools
- `ConfigMerger` — JSON config read/merge/write
- Bootstrapper scripts for user install and project setup
- Reference docs: Setup, Agents, GitHooks guides
- `CliServiceProvider` for minimal DI host

## Test Status (2026-03-25)

**641 passed, 0 failed (full run), 641 total**

- 31-01 added 23 new tests (TypeScriptStressTests: 5, TypeScriptDeterminismTests: 18)
- 31-03 added 1 new test (IngestTypeScriptAsync_produces_relative_file_paths_in_spans)
- 31-04 added 3 new tests (AuditLogger constructor, audit log verification, relative path spans)
- Full suite run confirms zero regressions

## Recent Decisions

| Decision | Rationale |
|----------|-----------|
| `ProjectSnapshotSummary` over full snapshots in solution | Eliminates triple in-memory accumulation |
| `ExcludeTestFiles = true` by default | Reduces noise in symbol graphs |
| CLI command architecture (CliRunner routing) | Clean separation of concerns for setup commands |
| Sentinel-based git hooks | Non-destructive hook management alongside existing hooks |
| TypeScript enum members from `symbol.exports` not `symbol.members` | TS Compiler API stores enum members in exports Map; members is always undefined for enums |
| Expanded manifest scope to tsconfig+package-lock | Complete change detection for TypeScript projects |
| Category property on TypeScriptIngestionException | Structured error responses without separate exception types |
| Early tsconfig.json existence validation | Fail-fast before sidecar spawn with tsconfig_invalid category |
| PipelineOverride for test isolation in TypeScript tests | Inject fixed-timestamp snapshots without Node.js sidecar; keeps CI fast |
| Fixed-timestamp snapshots for determinism tests | UtcNow in service overwrites CreatedAt; fixed timestamp ensures byte-identical serialization |
| Pass projectRoot into getSourceSpan for relative paths | Centralizes fix in one function rather than patching each call site |
| AuditLogger constructor-injected into IngestionTools | Consistent with other DI dependencies; supplements filter-level audit with domain metadata |
| Domain audit entry in arguments dictionary | AuditLogger.Log arguments carry symbolCount/skipped/path for JSONL audit trail |

## Blockers/Concerns

- None — phase 31 completed; 641 tests passing

## Session Continuity

Last session: 2026-03-25T20:20:00Z
Stopped at: Completed 31-04-PLAN.md
Resume file: None
