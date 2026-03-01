# Phase 9: Semantic Diff Engine - Context

**Gathered:** 2026-02-28
**Status:** Ready for planning

<domain>
## Phase Boundary

Core diff types and algorithm for comparing two `SymbolGraphSnapshot`s. Detects signature, nullability, constraint, accessibility, dependency, and doc comment changes. Produces a deterministic, immutable `SymbolDiff` result. MCP tools that consume this diff are Phase 11.

</domain>

<decisions>
## Implementation Decisions

### Change categorization
- Each change has both a **type** (added, removed, modified) and a **severity** (breaking, non-breaking, informational)
- Breaking change detection scoped to **public API only** — changes to public/protected symbols. Internal/private changes are non-breaking by definition
- Changes stored as a **flat list** with parent symbol ID for context (not hierarchical tree)
- Change categories: the 5 from roadmap (signature, nullability, constraints, accessibility, dependency) **plus doc comment changes** — since doc coverage is a v1.0 feature, tracking doc diffs is natural

### Diff output structure
- Include **summary statistics** — top-level counts by type and severity for quick triage
- **Complete result, consumer filters** — return full immutable diff, let MCP tools and consumers filter/slice as needed
- Each change entry carries **symbol IDs referencing both snapshots** for traceability (enables "show me the full before/after symbol" lookups)
- **MessagePack serializable** — consistent with existing snapshot serialization, enables caching and deterministic roundtrips

### Edge case handling
- **No rename detection** — treat as separate remove + add. Phase 11 tools can layer rename hints on top if needed
- **No move detection** — treat as remove from old location + add to new. Clean and deterministic
- **Error on incompatible snapshots** — snapshots must be from the same project. Comparing unrelated snapshots is a caller error
- **Require complete snapshots** — no partial snapshot handling. Avoids ambiguity between "removed" vs "not ingested"

### Change detail granularity
- Modified symbols include **both before/after values AND human-readable description** (e.g., ReturnType: 'string' → 'string?', Description: 'nullability changed')
- **Individual parameter diffs** — track each parameter: added, removed, type changed, name changed, default value changed
- **Direct dependencies only** — track what types/members a symbol directly references. Transitive impact is computable from the graph but not included in diff
- **Individual constraint diffs** — each generic constraint added/removed/changed tracked separately, consistent with parameter-level granularity

### Claude's Discretion
- Internal data structures and algorithms for the diff engine
- Performance optimization approaches
- Exact MessagePack contract design for SymbolDiff
- How to match symbols across snapshots (by ID, by qualified name, etc.)

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. The engine should follow the same patterns as existing Core domain types (immutable, deterministic, strongly typed).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 09-semantic-diff-engine*
*Context gathered: 2026-02-28*
