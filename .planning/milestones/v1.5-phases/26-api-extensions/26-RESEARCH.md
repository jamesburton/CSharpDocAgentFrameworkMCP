# Phase 26: API Extensions - Research

**Researched:** 2026-03-08
**Domain:** MCP tool surface extension (pagination, graph traversal, coverage metrics)
**Confidence:** HIGH

## Summary

Phase 26 extends the DocAgentFramework MCP tool surface with three capabilities: pagination for `get_references`, a new `find_implementations` tool, and a new `get_doc_coverage` tool. All three build on existing infrastructure with well-understood patterns.

The codebase already has pagination in `search_symbols` (offset/limit parameters), edge-indexed lookups via `SnapshotLookup` (EdgesByFrom/EdgesByTo dictionaries built in Phase 24), and doc coverage computation in `SolutionTools.ComputeDocCoverage`. The implementation work is primarily wiring -- connecting existing data structures to new MCP tool endpoints following established patterns. The `SymbolEdgeKind` enum already includes `Implements` and `Inherits` variants, and `NodeKind.Stub` filtering is already used in `SolutionTools`.

**Primary recommendation:** Follow the existing DocTools/SolutionTools patterns exactly. Extend `IKnowledgeQueryService` with new methods, add tool handlers in a new `ApiExtensionTools.cs` (or extend DocTools), and mirror the test patterns from `McpToolTests` and `GetReferencesAsyncTests`.

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| API-01 | get_references supports offset/limit pagination with total count in response envelope | Current `get_references` collects all edges into a `List<SymbolEdge>`, then serializes. Add offset/limit params with defaults that preserve backward compatibility. Paginate the collected list. Add `totalCount` to response. Pattern: mirror `search_symbols` pagination. |
| API-02 | find_implementations tool returns all types implementing a given interface or deriving from a base class, with stub node filtering | `SymbolEdgeKind.Implements` and `SymbolEdgeKind.Inherits` edges already exist in the graph. `SnapshotLookup.EdgesByTo` gives O(1) lookup for edges targeting a symbol. Filter results where `NodeKind == NodeKind.Stub`. |
| API-03 | get_doc_coverage tool returns documentation coverage metrics grouped by project, namespace, and symbol kind | `SolutionTools.ComputeDocCoverage` already computes per-project coverage. Extend to group by namespace (from `FullyQualifiedName` prefix) and by `SymbolKind`. Needs snapshot access via `SnapshotStore` or `IKnowledgeQueryService`. |
</phase_requirements>

## Standard Stack

### Core (already in project)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ModelContextProtocol.Server | (current) | `[McpServerTool]` / `[McpServerToolType]` attributes | MCP SDK for .NET |
| System.Text.Json | built-in | JSON serialization for tool responses | Project standard |
| FluentAssertions | (current) | Test assertions | Project standard |
| xUnit | (current) | Test framework | Project standard |

### No New Dependencies Needed
All three features can be built with existing libraries. No new NuGet packages required.

## Architecture Patterns

### Recommended Approach

#### API-01: get_references Pagination

The current `get_references` tool in `DocTools.cs` (line 295-369) already collects all edges into a `List<SymbolEdge>` before serializing. Adding pagination is straightforward:

1. Add `offset` (default 0) and `limit` (default 0, meaning "return all") parameters to `GetReferences` method
2. When `limit == 0`, return all edges (backward compatibility -- no silent truncation)
3. When `limit > 0`, slice the collected list with `.Skip(offset).Take(limit)` and add `totalCount` to the response envelope
4. The response shape when called without offset/limit must be identical to the current shape

**Key backward-compatibility constraint:** The success criteria explicitly require that calling without offset/limit returns the SAME response shape. This means `totalCount` should only appear in the paginated response, OR it should always be present (matching how `search_symbols` already includes `total`, `offset`, `limit` in every response). Looking at the current response:
```json
{ "total": N, "references": [...] }
```
The `total` field already exists. Adding `offset`/`limit` fields is additive and non-breaking.

#### API-02: find_implementations

This is a new tool that queries the edge graph. The pattern:

1. Accept a `symbolId` parameter (the interface or base class)
2. Look up `SnapshotLookup.EdgesByTo[symbolId]` to find all edges pointing TO this symbol
3. Filter to `SymbolEdgeKind.Implements` and `SymbolEdgeKind.Inherits` edges
4. The `From` side of each matching edge is the implementing/deriving type
5. Filter out nodes where `NodeKind == NodeKind.Stub`
6. Return the list with symbol metadata (id, displayName, kind, projectOrigin)

**Where to put it:** Could go in DocTools (it uses `IKnowledgeQueryService`) or in a new tool class. Since it needs snapshot access for stub filtering, it should either:
- Extend `IKnowledgeQueryService` with a `FindImplementationsAsync` method, OR
- Access the snapshot directly via `SnapshotStore`

Recommendation: Add a `FindImplementationsAsync` method to `IKnowledgeQueryService` / `KnowledgeQueryService` since it needs the `SnapshotLookup` cache. Put the tool handler in `DocTools` alongside `get_references`.

#### API-03: get_doc_coverage

This is a new tool that analyzes snapshot content. The pattern already exists in `SolutionTools.ComputeDocCoverage` (line 316-327):

```csharp
private static readonly HashSet<SymbolKind> s_docKinds = new()
{
    SymbolKind.Type, SymbolKind.Method, SymbolKind.Property,
    SymbolKind.Constructor, SymbolKind.Delegate, SymbolKind.Event, SymbolKind.Field,
};

private static readonly HashSet<Accessibility> s_docAccessibilities = new()
{
    Accessibility.Public, Accessibility.Protected, Accessibility.ProtectedInternal,
};
```

The tool needs to:
1. Load the current snapshot
2. Filter to `NodeKind.Real` nodes only
3. Group by project (`ProjectOrigin`), namespace (derived from `FullyQualifiedName`), and `SymbolKind`
4. For each group, compute: total candidates, documented count (has `Docs?.Summary`), percentage

**Where to put it:** Since this needs snapshot access but not ISearchIndex, it could go alongside `SolutionTools` (which already has `SnapshotStore` injection) or in DocTools (which has `IKnowledgeQueryService`). Recommendation: put in DocTools and add a method to `KnowledgeQueryService` that returns coverage data, leveraging the cached `SnapshotLookup`.

### Tool Registration Pattern

All existing tools follow this exact pattern:
```csharp
[McpServerToolType]
public sealed class DocTools
{
    [McpServerTool(Name = "tool_name"), Description("...")]
    public async Task<string> ToolMethod(
        [Description("...")] string param,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        // 1. Start activity for telemetry
        // 2. Validate input
        // 3. Call service
        // 4. Security checks (PathAllowlist, PromptInjectionScanner)
        // 5. Format response (FormatResponse with json/tron/markdown factories)
    }
}
```

### PathAllowlist Enforcement Pattern

Two patterns exist in the codebase:

1. **DocTools pattern** (for tools using IKnowledgeQueryService): PathAllowlist checks source spans only when `includeSourceSpans=true`. The allowlist verifies file paths, not snapshot access.

2. **ChangeTools/SolutionTools pattern** (for tools using SnapshotStore directly): PathAllowlist gates the entire operation by checking `_allowlist.IsAllowed(_snapshotStore.ArtifactsDir)`.

For the new tools:
- `find_implementations` and paginated `get_references` use `IKnowledgeQueryService` -- follow DocTools pattern
- `get_doc_coverage` needs snapshot data -- if it goes through `IKnowledgeQueryService`, follow DocTools pattern; if direct SnapshotStore, follow SolutionTools pattern

### Response Format Pattern

All tools support `json|markdown|tron` output via the `FormatResponse` helper. New tools must:
1. Add JSON serialization (anonymous type + `JsonSerializer.Serialize`)
2. Add TronSerializer methods for TRON format
3. Add markdown renderer methods

### Anti-Patterns to Avoid
- **Breaking backward compatibility:** Never change the response shape of `get_references` when called without the new parameters
- **Duplicating SnapshotLookup logic:** Use `GetOrBuildLookup` in KnowledgeQueryService, not separate caching
- **Skipping stub filtering:** `find_implementations` MUST filter `NodeKind.Stub` -- the success criteria explicitly require this
- **Including namespace nodes in coverage:** Only `s_docKinds` symbols with `s_docAccessibilities` are coverage candidates

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Edge lookup | Linear scan of snapshot.Edges | `SnapshotLookup.EdgesByTo` / `EdgesByFrom` | O(1) via Phase 24 dictionaries |
| Doc coverage logic | New coverage algorithm | Extend `SolutionTools.ComputeDocCoverage` pattern | Already proven correct |
| Namespace extraction | Complex parsing | Split `FullyQualifiedName` on last `.` | Matches existing codebase convention |
| Pagination | Custom pagination logic | `.Skip(offset).Take(Math.Min(limit, maxLimit))` | Standard LINQ, matches `search_symbols` |

## Common Pitfalls

### Pitfall 1: Breaking get_references backward compatibility
**What goes wrong:** Adding `totalCount`/`offset`/`limit` fields changes the JSON shape for callers that don't pass the new params
**Why it happens:** Eager refactoring of the response format
**How to avoid:** The current response already has `"total": N`. Keep this field. Add `offset` and `limit` fields -- they're additive. When no pagination params are passed, return ALL edges (no truncation). The `total` field should reflect the actual count of returned references (matching current behavior).
**Warning signs:** Existing test `McpToolTests.GetReferences_ValidId_ReturnsEdgesArray` checks `total == 2` and `references.length == 2`. This must still pass.

### Pitfall 2: Stub nodes in find_implementations results
**What goes wrong:** Returning stub nodes (external assembly references) alongside real implementations
**Why it happens:** Forgetting to check `NodeKind` after resolving edges
**How to avoid:** After finding implementing types via edges, look up each node in `SnapshotLookup.NodeById` and filter where `NodeKind != NodeKind.Stub`
**Warning signs:** Test should include a stub node implementing the interface and verify it's excluded

### Pitfall 3: Namespace extraction from FullyQualifiedName
**What goes wrong:** Incorrect namespace grouping when FQN is null or has no dots
**Why it happens:** Some nodes (especially top-level) may have null FQN or no namespace
**How to avoid:** Use `node.FullyQualifiedName?.LastIndexOf('.')` with a fallback to "(global)" or "(no namespace)"
**Warning signs:** Null reference exceptions in coverage computation

### Pitfall 4: IKnowledgeQueryService interface change
**What goes wrong:** Adding methods to the interface breaks existing implementations (test stubs)
**Why it happens:** `IKnowledgeQueryService` is implemented by `KnowledgeQueryService` and multiple test stubs (`StubKnowledgeQueryService`, `StaleIndexStub`, `InjectionDocStub`)
**How to avoid:** When adding `FindImplementationsAsync` or coverage methods, all test stubs in `McpToolTests.cs` must be updated. Consider adding default interface implementations or keeping new tool logic in the tool class itself (using SnapshotStore directly).
**Warning signs:** Compilation errors in test files after interface changes

### Pitfall 5: Dictionary ordering in coverage response
**What goes wrong:** Non-deterministic JSON output due to Dictionary iteration order
**Why it happens:** Grouping results into dictionaries without sorting
**How to avoid:** Sort groups by key (project name, namespace, symbol kind) before serialization
**Warning signs:** Flaky snapshot tests or comparison tests

## Code Examples

### Current get_references response shape (must preserve)
```csharp
// Source: DocTools.cs lines 344-358
var payload = new
{
    promptInjectionWarning = hasInjectionWarning,
    total = edges.Count,
    references = edges.Select(e => new
    {
        fromId = e.From.Value,
        toId = e.To.Value,
        edgeKind = e.Kind.ToString(),
        scope = e.Scope.ToString(),
        fromProject = nodeProjectCache.GetValueOrDefault(e.From),
        toProject = nodeProjectCache.GetValueOrDefault(e.To),
    }).ToList()
};
```

### Pagination pattern from search_symbols
```csharp
// Source: DocTools.cs lines 55-56, KnowledgeQueryService.cs line 65
[Description("Result offset for pagination")] int offset = 0,
[Description("Result limit (max 100)")] int limit = 20,

// In service:
var page = filtered.Skip(offset).Take(limit).ToList();
```

### Edge-based implementation lookup pattern
```csharp
// Source: KnowledgeQueryService.cs lines 107-128
// EdgesByTo gives edges pointing TO a symbol
if (lookup.EdgesByTo.TryGetValue(id, out var incomingEdges))
{
    foreach (var edge in incomingEdges)
    {
        if (edge.Kind == SymbolEdgeKind.Implements || edge.Kind == SymbolEdgeKind.Inherits)
        {
            // edge.From is the implementing/deriving type
            if (lookup.NodeById.TryGetValue(edge.From, out var node) && node.NodeKind != NodeKind.Stub)
            {
                // Include in results
            }
        }
    }
}
```

### Doc coverage computation pattern
```csharp
// Source: SolutionTools.cs lines 316-327
var docCandidates = projNodes
    .Where(n => s_docKinds.Contains(n.Kind) && s_docAccessibilities.Contains(n.Accessibility))
    .ToList();

if (docCandidates.Count == 0)
    return 0.0;

var documented = docCandidates.Count(n => n.Docs?.Summary is not null);
return Math.Round((double)documented / docCandidates.Count * 100.0, 1);
```

### Test stub pattern (McpToolTests.cs)
```csharp
// Source: McpToolTests.cs lines 51-74
private static DocTools CreateTools(
    IKnowledgeQueryService? svc = null,
    bool permissiveAllowlist = true,
    bool verboseErrors = false)
{
    var opts = new DocAgentServerOptions { VerboseErrors = verboseErrors };
    PathAllowlist allowlist;
    if (permissiveAllowlist)
    {
        var permissiveOpts = new DocAgentServerOptions { AllowedPaths = ["**"] };
        allowlist = new PathAllowlist(Options.Create(permissiveOpts));
    }
    else
    {
        allowlist = new PathAllowlist(Options.Create(opts));
    }

    return new DocTools(
        svc ?? new StubKnowledgeQueryService(),
        allowlist,
        NullLogger<DocTools>.Instance,
        Options.Create(opts));
}
```

## Key Domain Types Reference

### SymbolEdgeKind (Symbols.cs)
```
Contains, Inherits, Implements, Calls, References, Overrides, Returns
```
- `Implements`: from implementing type TO interface
- `Inherits`: from derived type TO base class

### NodeKind (Symbols.cs)
```
Real = 0,  // Discovered from project source
Stub = 1   // Synthesized from external assembly reference
```

### EdgeScope (Symbols.cs)
```
IntraProject = 0,   // Same project
CrossProject = 1,   // Different projects, same solution
External = 2        // One endpoint is a stub
```

### SymbolNode key fields for coverage
- `Kind`: SymbolKind enum
- `Accessibility`: Accessibility enum
- `Docs?.Summary`: non-null means documented
- `ProjectOrigin`: project name string
- `FullyQualifiedName`: dot-separated namespace path
- `NodeKind`: Real vs Stub

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit + FluentAssertions |
| Config file | tests/DocAgent.Tests/DocAgent.Tests.csproj |
| Quick run command | `dotnet test --filter "FullyQualifiedName~ApiExtension"` |
| Full suite command | `dotnet test` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| API-01 | get_references without offset/limit returns same shape | unit | `dotnet test --filter "FullyQualifiedName~McpToolTests.GetReferences_ValidId_ReturnsEdgesArray" -x` | Existing test |
| API-01 | get_references with offset/limit returns paginated envelope | unit | `dotnet test --filter "FullyQualifiedName~GetReferences_Paginated"` | Wave 0 |
| API-01 | get_references totalCount matches full edge count | unit | `dotnet test --filter "FullyQualifiedName~GetReferences_TotalCount"` | Wave 0 |
| API-02 | find_implementations returns implementing types | unit | `dotnet test --filter "FullyQualifiedName~FindImplementations_Returns"` | Wave 0 |
| API-02 | find_implementations excludes stub nodes | unit | `dotnet test --filter "FullyQualifiedName~FindImplementations_ExcludesStubs"` | Wave 0 |
| API-02 | find_implementations for non-interface returns empty | unit | `dotnet test --filter "FullyQualifiedName~FindImplementations_NonInterface"` | Wave 0 |
| API-03 | get_doc_coverage groups by project | unit | `dotnet test --filter "FullyQualifiedName~DocCoverage_GroupsByProject"` | Wave 0 |
| API-03 | get_doc_coverage groups by namespace and kind | unit | `dotnet test --filter "FullyQualifiedName~DocCoverage_GroupsByNamespace"` | Wave 0 |
| API-03 | get_doc_coverage excludes non-public symbols | unit | `dotnet test --filter "FullyQualifiedName~DocCoverage_ExcludesNonPublic"` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~ApiExtension" -x`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before /gsd:verify-work

### Wave 0 Gaps
- [ ] New test methods for paginated get_references in McpToolTests.cs or new test file
- [ ] New test methods for find_implementations tool
- [ ] New test methods for get_doc_coverage tool
- [ ] StubKnowledgeQueryService may need new method implementations if IKnowledgeQueryService is extended

## Open Questions

1. **Where to place new tool methods -- DocTools or new class?**
   - What we know: DocTools already has 5 tools. ChangeTools has 3. SolutionTools has 2.
   - What's unclear: Whether adding 2 more to DocTools makes it unwieldy (it's already 720 lines)
   - Recommendation: Add `find_implementations` to DocTools (it's a query tool like get_references). Add `get_doc_coverage` to DocTools or SolutionTools (it's coverage-related; SolutionTools already has ComputeDocCoverage helper). Planner should decide.

2. **Should IKnowledgeQueryService be extended or should new tools use SnapshotStore directly?**
   - What we know: Extending the interface requires updating 3+ test stubs. Using SnapshotStore directly avoids this but loses SnapshotLookup caching.
   - What's unclear: Whether the maintenance cost of interface changes outweighs architectural cleanliness
   - Recommendation: Keep `find_implementations` logic in the tool handler class, accessing SnapshotStore directly (like SolutionTools does). This avoids IKnowledgeQueryService changes. For `get_references` pagination, the change is to the DocTools handler only -- no interface change needed since edges are already fully collected.

## Sources

### Primary (HIGH confidence)
- DocTools.cs -- current get_references implementation, search_symbols pagination pattern
- KnowledgeQueryService.cs -- SnapshotLookup with EdgesByFrom/EdgesByTo dictionaries, GetReferencesAsync
- Symbols.cs -- SymbolEdgeKind enum (Implements, Inherits), NodeKind (Real, Stub), EdgeScope
- SolutionTools.cs -- ComputeDocCoverage helper, stub filtering pattern, SnapshotStore usage
- QueryTypes.cs -- ResponseEnvelope, SearchResultItem, SymbolDetail records
- McpToolTests.cs -- test patterns, StubKnowledgeQueryService, CreateTools helper
- GetReferencesAsyncTests.cs -- edge traversal test patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - no new dependencies, all patterns exist in codebase
- Architecture: HIGH - extending proven patterns with well-understood data structures
- Pitfalls: HIGH - backward compatibility risk is real but mitigatable with existing tests

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable domain, no external dependency changes)
