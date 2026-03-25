---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: TypeScript Language Support
status: Between milestones — ad-hoc feature work
stopped_at: Phase 30 context gathered
last_updated: "2026-03-25T09:40:18.308Z"
last_activity: 2026-03-20 — Session resumed
progress:
  total_phases: 4
  completed_phases: 2
  total_plans: 9
  completed_plans: 7
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
Last activity: 2026-03-20 — Session resumed

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

## Test Status (2026-03-20)

**488 passed, 2 failed, 490 total**

Failing tests:
1. `ChangeToolTests.ReviewChanges_PathDenied_ReturnsAccessDenied` — response shape mismatch (expects `"error"` property)
2. `RegressionGuardTests.SolutionIngestion_DoesNotRegressBeyondBaseline` — BenchmarkDotNet `ResultStatistics` null (environment issue)

## Recent Decisions

| Decision | Rationale |
|----------|-----------|
| `ProjectSnapshotSummary` over full snapshots in solution | Eliminates triple in-memory accumulation |
| `ExcludeTestFiles = true` by default | Reduces noise in symbol graphs |
| CLI command architecture (CliRunner routing) | Clean separation of concerns for setup commands |
| Sentinel-based git hooks | Non-destructive hook management alongside existing hooks |
| TypeScript enum members from `symbol.exports` not `symbol.members` | TS Compiler API stores enum members in exports Map; members is always undefined for enums |

## Blockers/Concerns

- 2 failing tests need investigation before next feature work
- `.continue-here.md` in phase 31 is stale (references v2.1.0 work as just completed)
- STATE.md and ROADMAP.md were out of date (now updated)

## Session Continuity

Last session: 2026-03-25T09:40:18.293Z
Stopped at: Phase 30 context gathered
Resume file: .planning/phases/30-mcp-integration-and-incremental-ingestion/30-CONTEXT.md
