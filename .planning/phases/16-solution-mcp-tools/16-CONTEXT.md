# Phase 16: Solution MCP Tools - Context

**Gathered:** 2026-03-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Expose two new solution-level MCP tools (`explain_solution` and `diff_snapshots`) and enforce PathAllowlist security on all solution operations. Existing single-project tools are NOT modified — this adds solution-level capabilities alongside them.

</domain>

<decisions>
## Implementation Decisions

### explain_solution Output
- Summary table format: each project as a row with name, node count, edge count, doc coverage %
- Project dependency DAG represented as adjacency list: JSON object mapping project -> dependencies array (e.g., `{"Web": ["Core", "Data"]}`)
- Doc coverage as simple percentage: one number per project (% of public types/members with XML docs)
- Stub node info: total count only — single number for total external/stub nodes across solution

### diff_snapshots Scope
- Per-project sections: group changes by project, each showing added/removed/modified symbols
- Dedicated cross-project edge changes section separate from per-project diffs
- Reuse existing v1.1 SemanticDiffEngine per-project, then aggregate results + add cross-project edge diff layer on top
- Two explicit snapshot version/hash parameters ('before' and 'after') — agent controls exactly what to compare

### Error & Edge Cases
- Single-project solutions: same JSON structure with empty sections (empty dependency array, note it's single-project). Predictable for agents.
- Projects added/removed between versions: dedicated 'Projects Added' / 'Projects Removed' sections at top of diff, then per-project symbol diffs for surviving projects
- Solution tools require SolutionSnapshot from ingest_solution — clear boundary, no fallback to single-project snapshots
- PathAllowlist violation: opaque not-found denial ("Solution not found"), matching DocTools/ChangeTools security pattern

### Consistency with Existing Tools
- Separate SolutionTools class alongside DocTools and ChangeTools — clean separation of concerns
- Underscore naming convention: `explain_solution`, `diff_snapshots` — matches existing `search_symbols`, `get_symbol`, `get_references`
- Response format matches DocTools pattern (content array with type/text structure)
- Solution path parameter accepts .sln file path only, consistent with ingest_solution

### Claude's Discretion
- Internal service layer structure (whether to create ISolutionQueryService or similar)
- Exact JSON field names and nesting within the content text
- How to compute doc coverage percentage (which symbol kinds count)
- DI registration and wiring details

</decisions>

<specifics>
## Specific Ideas

- The tools should feel like natural extensions of the existing MCP surface — an agent that already knows search_symbols and get_references should find explain_solution and diff_snapshots intuitive
- Adjacency list DAG format chosen specifically because it's easy for LLM agents to parse and reason about

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 16-solution-mcp-tools*
*Context gathered: 2026-03-02*
