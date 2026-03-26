---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: TypeScript Language Support
status: Between milestones — ad-hoc feature work
stopped_at: Completed 35-02-PLAN.md
last_updated: "2026-03-26T15:20:40.120Z"
last_activity: "2026-03-26 — Completed 35-01 (TS/C# contract alignment: typeParameterName, IsOptional, dormant enum removal)"
progress:
  total_phases: 8
  completed_phases: 8
  total_plans: 19
  completed_plans: 19
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
Last activity: 2026-03-26 — Completed 35-01 (TS/C# contract alignment: typeParameterName, IsOptional, dormant enum removal)

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

## Test Status (2026-03-26)

**657 passed, 0 failed (non-stress run), 659 total** (35-02: sidecar E2E tests correctly show as Skipped, 2 skipped)

- 31-01 added 23 new tests (TypeScriptStressTests: 5, TypeScriptDeterminismTests: 18)
- 31-03 added 1 new test (IngestTypeScriptAsync_produces_relative_file_paths_in_spans)
- 31-04 added 3 new tests (AuditLogger constructor, audit log verification, relative path spans)
- 32-01 zero regressions — 56 TypeScript tests pass, 570 non-TypeScript tests pass
- 32-02 added 8 new tests (6 TypeScriptDeserializationTests + 2 TypeScriptSidecarIntegrationTests)
- 33-01 added 5 new tests (NodeAvailabilityHealthCheckTests: 4 health check + 1 env var binding)
- 35-01 added 5 new tests (GenericConstraint typeParameterName, ParameterInfo IsOptional, SymbolEdgeKind InheritsFrom/Accepts rejection)
- 35-02 zero new tests; sidecar E2E tests now show as Skipped (was silently Passed) — honest CI output

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
| [property: JsonPropertyName(...)] on positional record params | `property:` target required for attr to apply to generated property that serializer reads |
| SidecarJsonOptions renamed from JsonOptions | Signals scope — prevents accidental reuse in MCP output path |
| allowIntegerValues: false on JsonStringEnumConverter | Forces immediate exception if TS sidecar regresses to numeric ordinals |
| DocCommentConverter.Write throws NotSupportedException | Read-only converter — MCP output uses separate serializer, never calls Write |
| TS SymbolEdgeKind removes Extends, keeps Inherits | Single Inherits = "Inherits" covers class inheritance; aligns with C# enum member name |
| Mirror SidecarJsonOptions in tests rather than exposing as internal | Minimal production API surface changes for test purposes; exact mirror of private options |
| GoldenFile_Snapshot_Matches_Reference uses exact counts | We control the golden file, so exact counts (18 nodes, 20 edges) catch dropped nodes/edges definitively |
| Sidecar integration tests use LuceneRAMDirectory | Avoids FSDirectory per-snapshot swapping issues; consistent with TypeScriptToolVerificationTests pattern |
| NodeAvailabilityHealthCheck returns Degraded not Unhealthy | Keeps /health at HTTP 200 so Aspire dashboard probe always succeeds even without Node.js |
| AppHost-level AddHealthChecks not available in Aspire.AppHost.Sdk | McpServer /health endpoint handles sidecar availability reporting instead |
| No .WaitFor(sidecar) in AppHost | Parallel startup with graceful degradation — McpServer starts independently |
| Retroactive VALIDATION.md files use `complete` status | Phase already verified; `draft` reserved for pre-execution files |
| Retroactive VERIFICATION.md justified by downstream phase success | Phases 29-33 all built on Phase 28 deliverables; success chain confirms Phase 28 functional |
| ParameterInfo.IsOptional added at end with default=false | Backward-compatible: existing 7-arg call sites compile without change; follows project convention of appending to record |
| GenericConstraint.name renamed to typeParameterName in TS | TS was the source of INT-01 silent data loss; C# already had correct JsonPropertyName |
| Removed InheritsFrom and Accepts from TS SymbolEdgeKind | Dormant values never emitted; removal eliminates INT-04 latent deserialization throw risk |
| Static [Fact(Skip=...)] for sidecar E2E tests | Honest CI output — tests show Skipped not silently Passed; skip message explains prerequisites |
| WithReference(sidecar) for Aspire dependency link | Creates dashboard dependency graph edge without WaitFor; NodeApp supports this extension method |

## Blockers/Concerns

- None — phase 35 complete; INT-02 and INT-03 from v2.0 audit closed

## Session Continuity

Last session: 2026-03-26T14:39:58.220Z
Stopped at: Completed 35-02-PLAN.md
Resume file: None
