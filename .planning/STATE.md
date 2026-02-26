# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-25)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** Phase 1 — Core Domain

## Current Position

Phase: 1 of 6 (Core Domain)
Plan: 1 of 3 in current phase
Status: In progress
Last activity: 2026-02-26 — Completed 01-01 (domain type expansion + SymbolId tests)

Progress: [█░░░░░░░░░] 6% (1/18 plans complete across all phases)

## Performance Metrics

**Velocity:**
- Total plans completed: 1
- Average duration: 16 min
- Total execution time: 0.27 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| Phase 1 - Core Domain | 1/3 | 16 min | 16 min |

**Recent Trend:**
- Last 5 plans: 01-01 (16m)
- Trend: baseline established

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Pre-phase]: Non-generic `ISymbolGraphBuilder` for V1 — simpler V1 contract
- [Pre-phase]: Stdio-only MCP transport for V1 — simplest security model
- [Pre-phase]: BM25 first via Lucene.Net, embeddings behind `IVectorIndex` interface only
- [Pre-phase]: Snapshot artifacts to `artifacts/` directory, file-based storage for V1
- [01-01]: PreviousIds as IReadOnlyList on SymbolNode (not a dedicated edge) for V1 simplicity
- [01-01]: Root Directory.Packages.props created — tests/ is outside src/ tree; CPM must be at common ancestor
- [01-01]: ContentHash on SymbolGraphSnapshot is nullable — set by persistence layer to avoid circular dependency
- [01-01]: Verify.Xunit 31.x requires xunit 2.9.x (upgraded from 2.7.1); Microsoft.NET.Test.Sdk 18.0.1 required for .NET 10 SDK testhost
- [01-01]: [UseVerify] in v31 is an assembly-level build attribute, not a class attribute — no per-class decoration needed

### Pending Todos

None.

### Blockers/Concerns

- [Research flag] Phase 2: MSBuildLocator isolation and AssemblyLoadContext boundary strategy needs confirmation during planning
- [Research flag] Phase 5: MCP SDK 1.0.0 released 2026-02-25 — verify exact `[McpServerTool]` attribute API and schema validation before plan-phase
- [Research flag] Phase 6: Semantic diff risk classification model for Analysis layer
- [Dependency] Roslyn version: current pin is 4.12.0, research recommends upgrade to 5.0.0 for C# 14 semantic APIs — confirm in Phase 2 plan
- [RESOLVED] FluentAssertions v7+ license change — kept at 6.12.1 (Apache 2.0) in Directory.Packages.props

## Session Continuity

Last session: 2026-02-26
Stopped at: Completed 01-01-PLAN.md — domain type expansion and SymbolId tests. Ready to run 01-02-PLAN.md.
Resume file: None
