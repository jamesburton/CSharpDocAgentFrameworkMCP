# Phase 22: Documentation Refresh - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Update Architecture.md, Plan.md, and Testing.md to accurately reflect the v1.0-v1.2 shipped codebase and current test suite. No new documentation files — refresh existing docs only.

</domain>

<decisions>
## Implementation Decisions

### Document depth and tone
- Concise reference style — tables, diagrams, key facts (match CLAUDE.md style)
- Target audience: developers picking up the codebase
- Reference CLAUDE.md rather than duplicate its content — docs add depth beyond the quick-start
- Include Mermaid diagrams where they aid understanding (pipeline flow, layer dependencies)

### Architecture.md structure
- Group 12 MCP tools by domain: DocTools, ChangeTools, SolutionTools, IngestionTools
- Show full pipeline with interface names (IProjectSource → IDocSource → ISymbolGraphBuilder → etc.)
- Brief security section with pointer to Security.md for details
- Include project dependency table showing which projects reference which

### Plan.md format
- Shipped summary for v1.0-v1.2 (accomplished milestones with key features) plus future plans for v1.3+
- List MCP tools delivered per milestone
- Preserve deferred/speculative items (polyglot, embeddings, query DSL) in a "Future" section

### Testing.md scope
- Strategy + how-to-run: test philosophy, category breakdown, commands, fixture patterns
- Table of test categories: category, count, what it validates, example test class
- Known Limitations section documenting environment-dependent test failures (MSBuild workspace tests)

### Claude's Discretion
- Exact Mermaid diagram content and layout
- Section ordering within each document
- How much cross-referencing between the three docs
- Whether to update Security.md or Worktrees.md (out of scope unless trivially stale)

</decisions>

<specifics>
## Specific Ideas

- Architecture.md should name all 6 projects and all 12 MCP tools (success criterion 1)
- Plan.md must have no phantom features or missing accomplishments (success criterion 2)
- Testing.md must state current test count and actual strategy (success criterion 3)
- Current state: ~330 total tests, 309 passing, 21 environment-dependent failures

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 22-documentation-refresh*
*Context gathered: 2026-03-03*
