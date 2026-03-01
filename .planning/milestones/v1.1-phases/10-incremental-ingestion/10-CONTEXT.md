# Phase 10: Incremental Ingestion - Context

**Gathered:** 2026-02-28
**Status:** Ready for planning

<domain>
## Phase Boundary

File change detection and partial re-ingestion — only re-process changed source files, preserving unchanged symbols from the previous snapshot. Produces a new SymbolGraphSnapshot identical to what a full re-ingestion would produce, with metadata tracking which files changed and which symbols were affected.

</domain>

<decisions>
## Implementation Decisions

### Change detection strategy
- Content hash comparison (SHA-256) for definitive change detection — immune to timestamp drift
- Persistent manifest (file → hash mapping) stored alongside the snapshot for fast comparison on subsequent runs
- Deleted files: symbols removed from the new snapshot (clean removal, no tombstoning)
- No dry-run mode for now — keep it simple, can be added later

### Merge & preservation
- New immutable snapshot built each run — combine unchanged symbols from previous + newly parsed symbols from changed files
- Edges recomputed only for changed files — edges between two unchanged files preserved as-is
- Previous snapshot retained for diffing (aligns with Phase 9's SymbolGraphDiffer)
- Partial classes/methods: if any file containing a partial type changes, re-parse ALL files containing that partial type to ensure completeness

### Change tracking metadata
- File-level change log per ingestion run: which files were added/modified/removed, plus which symbols were affected per file
- Metadata embedded in SymbolGraphSnapshot (IngestionMetadata property) — travels with the snapshot
- Each ingestion run gets a GUID + timestamp for traceability
- Internal only for now — no MCP tool exposure until a future phase requires it

### Correctness guarantees
- Hard invariant: incremental snapshot must be identical to full re-ingestion (per roadmap success criteria)
- Test-time verification: tests run both paths and compare — production trusts the incremental path
- Fallback to full re-ingestion if incremental detects an inconsistency it can't handle
- Force-full option on the ingestion API as escape hatch for periodic full refreshes

### Claude's Discretion
- Hash algorithm implementation details (streaming vs full-read)
- Manifest storage format (JSON, binary, etc.)
- Internal caching strategy during merge
- Edge recomputation algorithm specifics
- Exact IngestionMetadata type shape (beyond the required fields)

</decisions>

<specifics>
## Specific Ideas

- Correctness guarantee is non-negotiable — the roadmap success criteria explicitly require identical output
- Must integrate cleanly with existing SymbolGraphSnapshot immutable pattern
- Change metadata should be lightweight enough to not bloat snapshots
- Partial class handling is C#-specific and important for real-world codebases

</specifics>

<deferred>
## Deferred Ideas

- MCP tool for querying change history ("what changed since last run?") — future phase
- Dry-run / impact preview mode — future enhancement
- Filesystem watcher for real-time change detection — separate phase
- Symbol-level diff in metadata (overlaps with Phase 9's SymbolGraphDiffer) — not needed here

</deferred>

---

*Phase: 10-incremental-ingestion*
*Context gathered: 2026-02-28*
