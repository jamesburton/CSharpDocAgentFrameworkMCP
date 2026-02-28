# Phase 3: BM25 Search Index - Context

**Gathered:** 2026-02-26
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the stub `InMemorySearchIndex` with a Lucene.Net BM25-based `ISearchIndex` implementation. Symbols and documentation text are searchable with ranked results and CamelCase-aware tokenization. The index persists alongside snapshot artifacts and reloads without re-ingestion.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation decisions deferred to Claude. The roadmap success criteria are clear and sufficient:

- **Search behavior**: BM25 ranking with symbol name weighted higher than doc comment text. Exact matches rank above partial token matches. Partial CamelCase queries resolve correctly (e.g., `getRef` finds `GetReferences`).
- **Tokenization**: CamelCase splitting with acronym awareness (e.g., `XMLParser` → `XML` + `Parser`). Case-insensitive matching. Standard Lucene analyzers extended with custom CamelCase tokenizer.
- **Index persistence**: Lucene index segments stored alongside `.msgpack` snapshot artifacts. Index rebuilt from snapshot if segments missing or version mismatch. No separate versioning scheme — tied to snapshot content hash.

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. Follow Lucene.Net conventions and the project's existing patterns from Phase 1-2.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 03-bm25-search-index*
*Context gathered: 2026-02-26*
