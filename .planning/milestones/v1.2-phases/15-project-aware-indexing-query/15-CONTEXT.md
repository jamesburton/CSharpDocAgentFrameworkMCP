# Phase 15: Project-Aware Indexing & Query - Context

**Gathered:** 2026-03-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Extend the search index and query services so BM25 results carry project attribution and agents can filter results to a single project or query cross-project references — without breaking existing query contracts. Creating new MCP tools or solution-level overview capabilities are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Project filter design
- Exact match on project name (case-sensitive)
- Invalid project name returns empty results (no special error)
- Single project filter per call (no multi-project array)
- Optional parameter on existing `search_symbols` method (no new method/overload)

### Cross-project edge query
- `crossProjectOnly` returns edges in both directions (callers and callees across project boundaries)
- Direct edges only — no transitive graph walking
- Each edge result includes source and target project names
- `crossProjectOnly` is a simple boolean flag (no target project narrowing)

### Result ranking & attribution
- Pure BM25 ranking with no project bias
- `ProjectName` string property added to `SearchResult` type
- Ambiguous fully qualified names (same FQN in multiple projects) require project qualifier; return error listing which projects have the symbol
- Each result is one symbol from one project; duplicate FQNs appear as separate results

### Backward compatibility
- Transparent upgrade: existing calls work identically, just return results from all projects when no filter specified
- Extend existing `ISearchIndex` methods with optional parameters (defaults preserve current behavior)
- Update existing tests to verify backward compat + add new tests for project filtering and cross-project queries
- Expose `project` filter parameter in MCP tool schema for `search_symbols`

### Claude's Discretion
- Internal index structure changes needed to support project attribution
- How to efficiently partition or tag index entries by project
- EdgeScope enum design and storage
- Error message format for ambiguous FQN resolution

</decisions>

<specifics>
## Specific Ideas

- Success criteria explicitly require `search_symbols` with optional `project` filter, `get_symbol` resolving across any project, and `get_references` with `crossProjectOnly` flag
- The approach should feel like a natural extension of existing single-project behavior — agents that don't care about projects shouldn't notice any difference

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 15-project-aware-indexing-query*
*Context gathered: 2026-03-01*
