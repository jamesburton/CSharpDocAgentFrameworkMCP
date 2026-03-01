# Phase 13: Core Domain Extensions - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Extend existing domain types (`SymbolNode`, `SymbolEdge`, `SymbolGraphSnapshot`) with solution-level identity, cross-project edge scoping, and stub-node flags. All changes must be backward-compatible with v1.0/v1.1 MessagePack artifacts. This phase adds **types and fields only** — population logic belongs in Phase 14 (ingestion).

</domain>

<decisions>
## Implementation Decisions

### ProjectEntry & Solution Modeling
- Claude's Discretion: DAG model (ProjectEdge collection vs adjacency list on ProjectEntry)
- Claude's Discretion: ProjectEntry metadata richness (minimal vs rich with TFM, output type, NuGet refs)
- Claude's Discretion: SolutionSnapshot shape (wrapper around per-project snapshots vs single merged graph)
- Claude's Discretion: ProjectOrigin type on SymbolNode (simple string vs typed ProjectId reference)

### Stub Node Design
- Claude's Discretion: Stub information depth (type-level only vs type + public members)
- Claude's Discretion: Stub flag mechanism (IsStub bool, NodeKind discriminator, or both)
- Claude's Discretion: Stub creation strategy (eager for all referenced types vs lazy for edge targets only)
- Claude's Discretion: BM25 index treatment of stubs (completely excluded vs included with lower weight)

### EdgeScope & Cross-Project Edges
- Claude's Discretion: EdgeScope enum values (binary IntraProject/CrossProject vs ternary with External)
- Claude's Discretion: Additional edge metadata (scope only vs denormalized project names)
- Claude's Discretion: Scope computation timing (stored at ingestion vs derived at query time)
- Kind and Scope are orthogonal — Claude's Discretion on exact approach but no combinatorial explosion of composite kinds
- Phase 13 adds types/fields only; detection/population logic belongs in Phase 14

### Backward Compatibility
- Claude's Discretion: Test compatibility strictness (compile+pass unchanged vs minor assertion updates OK) — guided by success criteria "pass without modification"
- Claude's Discretion: MessagePack serialization approach (explicit Key indices vs follow existing convention)
- Claude's Discretion: Nullability pattern (C# nullable references vs Option wrapper) — project has nullable enabled
- Claude's Discretion: Whether to version-bump SymbolGraphSnapshot to v1.2

### Claude's Discretion
All four areas were discussed and the user delegated all implementation decisions to Claude. Claude has full flexibility to choose approaches that best fit:
- Existing codebase patterns and conventions
- Downstream phase needs (especially Phase 14 ingestion and Phase 15 querying)
- The 6 GRAPH requirements (GRAPH-01 through GRAPH-06)
- The success criteria requiring 220+ existing tests to pass without modification

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. User trusts Claude to make decisions aligned with existing codebase patterns and the success criteria defined in ROADMAP.md.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 13-core-domain-extensions*
*Context gathered: 2026-03-01*
