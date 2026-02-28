# Phase 4: Query Facade - Context

**Gathered:** 2026-02-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire `IKnowledgeQueryService` over the BM25 search index and snapshot store. All query operations (`SearchAsync`, `GetSymbolAsync`, `DiffAsync`) are testable through the facade without a running MCP server. This phase delivers the business logic layer — MCP tool wiring is Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Search behavior
- Filter by `SymbolKind` — optional parameter, agents often want "find me methods named X"
- Pagination via offset + limit (skip/take pattern), stateless
- Default max results: 20
- Accept optional snapshot version — default to latest, but allow pinning for reproducible queries and diff workflows

### Diff semantics
- A symbol is "modified" if its signature, accessibility, return type, parameters, OR doc comment changed
- Classification is type-only: Added, Removed, Modified — no severity classification (that's Phase 6 analysis)
- Track renames via PreviousIds — show "renamed from X to Y" instead of "removed X, added Y"
- Diff results include SymbolId + change type + brief summary of what changed (e.g., "return type: void → Task"), not full SymbolNode pairs. Caller uses GetSymbolAsync for full details.

### Error handling
- Structured result types throughout — no exceptions for expected failures (not found, stale index, missing snapshot)
- QueryResult<T> pattern with success/failure status
- Stale index: return results with a staleness warning flag — results are still useful, caller decides whether to re-index
- GetSymbolAsync with non-existent SymbolId: returns not-found result (consistent QueryResult pattern)
- DiffAsync validates snapshot existence internally — returns structured error if either snapshot is missing, no pre-validation required by caller

### Response shape
- Search results include BM25 relevance score (already computed, cheap to expose)
- Search results include match context snippet (doc summary excerpt or symbol signature) so agents can decide which result to drill into
- GetSymbolAsync returns the SymbolNode plus navigation hints: parent SymbolId, child SymbolIds, related symbol IDs — agents follow links without loading the whole graph
- Standard response envelope on all facade methods: snapshot version, timestamp, staleness flag, query duration

### Claude's Discretion
- Exact QueryResult<T> type design and error code enumeration
- Internal caching strategy (if any)
- How match context snippets are extracted from indexed content
- Navigation hint depth (direct children only, or also siblings)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. The facade should be a clean, testable abstraction that the MCP tools in Phase 5 can call directly.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 04-query-facade*
*Context gathered: 2026-02-26*
