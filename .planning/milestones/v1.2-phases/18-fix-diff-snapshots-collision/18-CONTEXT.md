# Phase 18: Fix diff_snapshots Tool Name Collision - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Resolve duplicate MCP tool name `diff_snapshots` between DocTools (Phase 5, per-symbol diff) and SolutionTools (Phase 16, per-project solution diff). The fix is a rename of the SolutionTools tool plus description update. DocTools remains unchanged.

</domain>

<decisions>
## Implementation Decisions

### Tool naming
- Rename SolutionTools tool from `diff_snapshots` to `diff_solution_snapshots`
- Follows existing naming pattern (verb_scope_noun)
- DocTools `diff_snapshots` name is unchanged

### Tool description
- Update SolutionTools `diff_solution_snapshots` description to clearly state it's solution-level: per-project diffs, cross-project edge changes
- Do NOT change DocTools `diff_snapshots` description — minimize blast radius

### Claude's Discretion
- Exact wording of the updated tool description
- Whether test assertion strings need updating for the new tool name
- Any documentation references to update

</decisions>

<specifics>
## Specific Ideas

No specific requirements — the audit identified the exact fix needed (one-line attribute rename + description update).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 18-fix-diff-snapshots-collision*
*Context gathered: 2026-03-02*
