# Phase 32: JSON Contract Alignment (TS ↔ C# Deserialization) - Context

**Gathered:** 2026-03-25
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix all JSON deserialization mismatches between TypeScript sidecar output and C# domain types so the real sidecar→C# pipeline produces correct SymbolGraphSnapshots. Three categories of mismatch: property name differences (sourceId/targetId vs From/To, docComment vs Docs), SymbolEdgeKind enum ordinal drift, and missing C#-only fields.

</domain>

<decisions>
## Implementation Decisions

### Fix direction (property names)
- Fix on C# side — add `[JsonPropertyName]` attributes to C# domain types to accept TS camelCase names
- Full contract mapping — add `[JsonPropertyName]` to every property on SymbolNode, SymbolEdge, DocComment, SourceSpan, and related records (not just the mismatched ones)
- Separate serialization options — MCP tool output stays PascalCase (its own JsonSerializerOptions), sidecar deserialization uses PropertyNameCaseInsensitive=true + the attrs
- Claude's discretion on approach: DTO layer, custom JsonConverter, or attrs with separate MCP options — pick whichever minimizes risk to MCP API consumers

### Enum alignment strategy
- String-based serialization for ALL enums in the sidecar contract (SymbolEdgeKind, SymbolKind, Accessibility, EdgeScope, NodeKind)
- TS sidecar emits C# enum member names as strings (e.g., "Inherits" not "Extends", "Contains" not numeric 0)
- Map TS `Extends` edge kind → C# `Inherits` (semantically correct for class inheritance)
- JsonStringEnumConverter on C# deserialization side
- Future-proofs against any ordinal drift between TS and C# enums

### E2E test approach
- Both golden JSON file test AND real sidecar integration test
- Golden file: Captured from real sidecar output against existing TS test fixtures (not hand-crafted)
- Real sidecar test: Gated behind `RUN_SIDECAR_TESTS=true` environment variable (opt-in, keeps default CI fast)
- Assertions must cover all four categories:
  1. Edge integrity — From/To non-null, edge kinds correct (Inherits not Returns), edge count matches
  2. Doc preservation — Docs non-null for documented symbols, summary text matches
  3. Full snapshot comparison — deserialized snapshot matches reference (structural equality)
  4. Query tool validation — search_symbols, get_references etc. return correct results against deserialized snapshot

### Backward compatibility (missing fields)
- C# defaults are sufficient for TS-missing fields — PreviousIds defaults to empty list, ProjectOrigin defaults to null
- EdgeScope defaults to IntraProject (correct for single TS project)
- No TS changes needed for these fields
- No contract versioning mechanism needed — string enums + golden file + E2E test are sufficient for an internal contract

### Claude's Discretion
- Exact approach to isolating sidecar JsonSerializerOptions from MCP serialization (DTO layer vs converter vs separate options)
- Internal test helper organization for golden file capture/comparison
- Whether to update existing PipelineOverride tests to also exercise the deserialization path

</decisions>

<specifics>
## Specific Ideas

- The golden JSON file should be captured from the actual sidecar running against existing test fixtures (src/ts-symbol-extractor/tests/fixtures/) — this ensures the test validates the real contract
- String enum names must match C# member names exactly (PascalCase: "Inherits", "Contains", "Implements") for native JsonStringEnumConverter compatibility
- The RUN_SIDECAR_TESTS env var pattern keeps CI fast by default while enabling full pipeline validation on demand

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- `TypeScriptIngestionService.JsonOptions` (line 34-37): Current deserialization config — needs JsonStringEnumConverter added
- `PipelineOverride` pattern: Test seam that bypasses sidecar — golden file tests should bypass this to test real deserialization
- Existing TS test fixtures in `src/ts-symbol-extractor/tests/fixtures/`: Source for golden file capture
- `SymbolGraphSnapshot` record and related types in `src/DocAgent.Core/Symbols.cs`: Target for [JsonPropertyName] attrs

### Established Patterns
- `PropertyNameCaseInsensitive = true` on TypeScriptIngestionService.JsonOptions
- MCP tool serialization uses separate JsonSerializerOptions (PascalCase output)
- xUnit test categories with `[Trait]` for test filtering
- Environment-gated tests not yet in codebase — new pattern for this phase

### Integration Points
- `src/DocAgent.Core/Symbols.cs`: Add [JsonPropertyName] attributes to records
- `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs`: Update JsonOptions with JsonStringEnumConverter
- `src/ts-symbol-extractor/src/types.ts`: Update enum values to emit C# member names as strings
- `src/ts-symbol-extractor/src/extractor.ts` (and related): Update edge kind assignments to use C# names
- `src/DocAgent.Tests/`: New deserialization and E2E test classes

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 32-json-contract-alignment*
*Context gathered: 2026-03-25*
