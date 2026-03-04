# Phase 21: Code and Audit Cleanup - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Remove stale code artifacts, resolve v1.2 audit issues, and fix integration wiring gaps identified in the v1.3 milestone audit. This is codebase hygiene — no new features, no behavioral changes beyond fixing broken wiring.

</domain>

<decisions>
## Implementation Decisions

### Audit Artifact Cleanup
- Claude's discretion on what constitutes "resolved" for v1.2 audit artifacts
- Claude decides whether to archive audit files to milestones/ or keep in .planning/
- No specific files flagged by user — scan and fix whatever's found

### Benchmark Wiring Fix
- Claude decides best approach to fix IncrementalNoChange benchmark (wrap in GlobalSetup vs add separate benchmark vs other)
- Goal: the incremental skip path must actually be exercised in benchmarks
- Claude decides whether to run benchmarks and capture real baseline values or leave placeholders

### Stale Comment Scope
- Scan entire codebase for TODO/FIXME/stub comments, not just the 2 specified
- Triage each: remove stale ones (reference completed work), keep legitimate ones
- Claude determines staleness based on context of each comment
- Report what was found and what was removed

### Tech Debt Triage
- Claude triages the 8 audit tech debt items by effort vs impact
- Thorough cleanup is acceptable — include quick wins beyond strict success criteria
- No hard constraint on phase size; quality over speed

### Claude's Discretion
- Audit artifact cleanup scope and file organization
- Benchmark fix approach (wrap vs restructure vs new benchmark)
- Whether to capture real baseline values
- Which tech debt items to include based on effort/impact
- Staleness determination for each TODO/stub found

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. User trusts Claude's judgment on all implementation details for this cleanup phase.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 21-code-and-audit-cleanup*
*Context gathered: 2026-03-03*
