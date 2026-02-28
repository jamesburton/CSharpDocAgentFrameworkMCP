# Phase 1: Core Domain - Context

**Gathered:** 2026-02-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Lock all domain contracts — SymbolId spec, snapshot schema, and interface definitions — so every downstream component (ingestion, indexing, query, MCP) builds against a stable foundation. No implementations beyond the contracts themselves.

</domain>

<decisions>
## Implementation Decisions

### SymbolId Identity
- Use Roslyn XML documentation comment ID format (e.g., `T:Namespace.Type`, `M:Namespace.Type.Method(System.String)`)
- Case-sensitive — match XML doc ID spec exactly; case-insensitive matching is a search-layer concern
- Rename tracking via `PreviousIds` — approach (list on SymbolNode vs. dedicated edge) is Claude's discretion
- Assembly prefix decision (e.g., `MyAssembly|T:...` vs. bare XML doc ID) is Claude's discretion based on multi-assembly scenarios

### Snapshot Schema
- Multi-format serialization: MessagePack (default), System.Text.Json (debugging), and TRON (middle ground)
- Serialization format should be pluggable — a format enum or strategy pattern
- Content hash algorithm: xxHash (non-cryptographic, fast change detection)
- Schema versioning: semver string (e.g., `"1.0.0"`) — already scaffolded as `string SchemaVersion`
- Add `ContentHash` field (xxHash of serialized content) to `SymbolGraphSnapshot`
- Add `ProjectName` field (source project identifier) to `SymbolGraphSnapshot`

### Interface Surface
- No pagination on `ISearchIndex.SearchAsync` for now — paginate at MCP tool layer later
- Primary return type: `IAsyncEnumerable<T>` for search operations
- Provide convenience extension method that materializes `IAsyncEnumerable<T>` to `Task<IReadOnlyList<T>>`
- Add `GetReferencesAsync(SymbolId id, CancellationToken ct)` to `IKnowledgeQueryService` — aligns with MCP `get_references` tool
- `IVectorIndex` stub shape is Claude's discretion (don't over-commit to v2 API surface)

### Edge & Node Model
- Expand `SymbolKind` enum comprehensively: add Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter
- Expand `DocComment` record: add Exceptions (`IReadOnlyList<(string Type, string Description)>`), SeeAlso (`IReadOnlyList<string>`), TypeParams (`IReadOnlyDictionary<string, string>`)
- Add `Accessibility` enum to `SymbolNode` (Public, Internal, Protected, Private, ProtectedInternal, PrivateProtected)
- Expand `SymbolEdgeKind`: add Overrides, Returns

### Claude's Discretion
- PreviousIds tracking approach (list on SymbolNode vs. RenamedFrom edge)
- Assembly prefix on SymbolId (bare XML doc ID vs. assembly-qualified)
- IVectorIndex stub shape (minimal marker vs. mirrored ISearchIndex)

</decisions>

<specifics>
## Specific Ideas

- TRON format (https://tron-format.github.io/) as a middle-ground serialization option between JSON readability and MessagePack compactness
- IAsyncEnumerable as first-class return type with a wrapping extension for materialization — not the other way around

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-core-domain*
*Context gathered: 2026-02-26*
