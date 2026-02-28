# Phase 11: Change Intelligence & Review - Context

**Gathered:** 2026-02-28
**Status:** Ready for planning

<domain>
## Phase Boundary

MCP tools that expose the semantic diff engine (Phase 9) to consumers: `review_changes`, `find_breaking_changes`, and `explain_change`. Includes unusual change detection with remediation suggestions. The diff engine itself is already built — this phase wraps it in actionable, consumable tool interfaces.

</domain>

<decisions>
## Implementation Decisions

### Change Severity & Classification
- Breaking changes defined by **public API surface only** (public/protected symbols). Internal changes are non-breaking.
- Three-tier severity system: **Breaking / Warning / Info**
- "Potentially Breaking" label for risky-but-not-technically-breaking changes (e.g., accessibility widening, adding optional params). Maps to Warning tier.
- Trivial changes (doc-only, whitespace, member reordering) filtered out by default; include only when verbose mode requested.

### Review Findings Format
- `review_changes` groups findings **by severity, then by symbol** — breaking changes surface first for priority-driven reading.
- Each finding includes **actionable remediation suggestions** (e.g., "Add overload with old signature for backward compat").
- Output includes a **summary section** (counts per severity, overall risk assessment) followed by detailed findings.
- `find_breaking_changes` is a **separate dedicated tool**, not a filter on `review_changes`. Cleaner API for CI/automation use cases.

### Explanation Style
- Target audience: **developer reviewing a PR** — assumes C# knowledge, uses code snippets, concise but precise.
- **Always show before/after code snippets** for every change (old signature → new signature).
- **Include impact scope**: list dependent symbols affected by the change ("This change affects 3 callers: X, Y, Z").
- **Include "why this matters"** natural language context (e.g., "This parameter type change will cause compile errors in any caller passing a string").

### Unusual Change Detection
- Flag all four patterns proactively:
  - Accessibility widening (private → public, internal → public)
  - Nullability regression (non-nullable → nullable on return types/parameters)
  - Mass signature changes (many methods in one type changing simultaneously)
  - Constraint removal (generic constraints being removed)
- Unusual patterns reported **proactively in review_changes** output as a dedicated section — not a separate tool.
- Remediation is **text-based suggestions** (describe what to fix), not code patches or worktree creation.
- Thresholds use **sensible defaults** (e.g., >5 signature changes in one type = mass change). Not configurable via parameters.

### Claude's Discretion
- Exact threshold values for "mass change" detection
- Internal data structures for change classification
- How to format before/after code snippets (inline vs block)
- Error handling when snapshots are incompatible

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 11-change-intelligence-review*
*Context gathered: 2026-02-28*
