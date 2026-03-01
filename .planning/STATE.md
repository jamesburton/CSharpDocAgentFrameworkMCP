---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: unknown
last_updated: "2026-03-01T11:02:35.696Z"
progress:
  total_phases: 4
  completed_phases: 4
  total_plans: 9
  completed_plans: 9
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-02-28)

**Core value:** Agents can query a stable, compiler-grade symbol graph of any .NET codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.
**Current focus:** v1.1 Semantic Diff & Change Intelligence — 3 phases planned

## Current Position

Phase: 12 of 12 — ChangeTools Security Gate (in progress)
Plan: 1 of 1 plans executed (12-01 complete)
Status: Phase 12 complete — plan 01 executed
Last activity: 2026-03-01 — Phase 12 plan 01 executed

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Full decision history archived in milestones/v1.0-ROADMAP.md.

### Pending Todos

None.

### Blockers/Concerns

None.

## Session Continuity

Last session: 2026-03-01
Stopped at: Completed 12-01-PLAN.md
Resume file: none (phase 12 complete)

### Decisions (09-01)

- SymbolNode extended with ReturnType, Parameters, GenericConstraints at end of record
- DiffTypes.cs uses per-category nullable detail fields (MessagePack ContractlessStandardResolver safe)
- RoslynSymbolGraphBuilder ExtractSignatureFields dispatches per Roslyn symbol type

### Decisions (09-02)

- SymbolGraphDiffer is a public static class — stateless utility, no DI needed
- Nullability heuristic: IsOnlyNullabilityDiff strips trailing '?' — prevents double-reporting with Signature
- Added symbols always NonBreaking regardless of visibility (additive changes are safe)
- Dependency edge grouping by (From,To) pair; Kind changes are modifications not remove+add

### Decisions (10-01)

- IngestionMetadata? added as last positional param of SymbolGraphSnapshot for ContractlessStandardResolver backward compatibility
- FileHasher is a public static class (stateless utility); ManifestDiff record with HasChanges and ChangedFiles computed properties
- SHA-256 via SHA256.HashDataAsync (streaming async); lowercase hex output

### Decisions (10-02)

- RemovedFiles included in change detection set alongside ChangedFiles to trigger project re-parse for deleted .cs files
- BuildOverride hook uses (ProjectInventory, DocInputSet, CancellationToken) signature matching ISymbolGraphBuilder.BuildAsync
- SnapshotStore.ArtifactsDir exposed as public property to avoid constructor coupling
- forceFullReingestion added as last default parameter to IIngestionService for backward compatibility

### Decisions (09-03)

- DiffTestHelpers uses BuildSnapshot overloads with optional projectName for incompatible-snapshot tests
- DiffDeterminismTests uses ContractlessStandardResolver matching existing SnapshotSerializationTests pattern

### Decisions (10-03)

- ContentHashedBuilder (BuildOverride fallback) used instead of real Roslyn — avoids MSBuild SDK resolution fragility per plan fallback guidance
- Fresh artifacts directory per independent run prevents manifest cross-contamination between full and incremental paths in tests
- Node ID encodes relPath + SHA-256 prefix — models real re-parse behavior (changed content yields different node IDs)

### Decisions (11-02)

- ExplainChangeDetail is a private sealed record inside ChangeTools — avoids leaking DTOs to public namespace
- ImpactScope in review_changes uses all edges where To==symbolId; explain_change scopes to Calls edges only
- Tron format for explain_change uses inline Utf8JsonWriter (not TronSerializer) since it serializes a different schema shape

### Decisions (11-01)

- AccessibilityRank dictionary maps 6-value Accessibility enum (no File value); Private=0 through Public=5
- Unusual symbol IDs tracked in HashSet for O(1) lookup during severity escalation
- MassSignatureChange groups by ParentSymbolId.Value — only fires when ParentSymbolId is non-null
- NullabilityRegression: checks OldAnnotation not ending with '?' and NewAnnotation does, using NullabilityChangeDetail fields

### Decisions (12-01)

- PathAllowlist guard checks _snapshotStore.ArtifactsDir once per method, not once per LoadAsync call
- Uses QueryErrorKind.NotFound for opaque denial matching DocTools pattern (not_found error code)
- ExplainChange test uses SaveBreakingPairAsync — guard fires before load so symbol existence is irrelevant
