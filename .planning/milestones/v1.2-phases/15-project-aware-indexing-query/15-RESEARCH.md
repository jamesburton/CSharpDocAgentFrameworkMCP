# Phase 15: Project-Aware Indexing & Query - Research

**Researched:** 2026-03-01
**Domain:** .NET / C# — BM25 search index extension with project attribution and cross-project edge filtering
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Project filter design
- Exact match on project name (case-sensitive)
- Invalid project name returns empty results (no special error)
- Single project filter per call (no multi-project array)
- Optional parameter on existing `search_symbols` method (no new method/overload)

#### Cross-project edge query
- `crossProjectOnly` returns edges in both directions (callers and callees across project boundaries)
- Direct edges only — no transitive graph walking
- Each edge result includes source and target project names
- `crossProjectOnly` is a simple boolean flag (no target project narrowing)

#### Result ranking & attribution
- Pure BM25 ranking with no project bias
- `ProjectName` string property added to `SearchResult` type
- Ambiguous fully qualified names (same FQN in multiple projects) require project qualifier; return error listing which projects have the symbol
- Each result is one symbol from one project; duplicate FQNs appear as separate results

#### Backward compatibility
- Transparent upgrade: existing calls work identically, just return results from all projects when no filter specified
- Extend existing `ISearchIndex` methods with optional parameters (defaults preserve current behavior)
- Update existing tests to verify backward compat + add new tests for project filtering and cross-project queries
- Expose `project` filter parameter in MCP tool schema for `search_symbols`

### Claude's Discretion
- Internal index structure changes needed to support project attribution
- How to efficiently partition or tag index entries by project
- EdgeScope enum design and storage
- Error message format for ambiguous FQN resolution

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TOOLS-01 | `search_symbols` returns results from all projects in a solution | Add `projectName` field to Lucene documents and `SearchResultItem`; KnowledgeQueryService applies post-search project filter using ProjectOrigin from SymbolNode |
| TOOLS-02 | `get_symbol` resolves by FQN across any project in the solution | SymbolId already encodes project origin in v1.2 snapshots; KnowledgeQueryService.GetSymbolAsync already does `_index.GetAsync(id)`; ambiguous FQN (same FQN in multiple projects) needs dedicated error path |
| TOOLS-03 | `get_references` spans project boundaries | GetReferencesAsync already returns all edges bidirectionally; add `crossProjectOnly` bool filter that restricts to `EdgeScope.CrossProject` edges; each edge result includes FromProject and ToProject from SymbolNode.ProjectOrigin lookups |
| TOOLS-06 | Existing tools accept optional `project` filter to scope results | `IKnowledgeQueryService.SearchAsync` already has `projectFilter` parameter (accepted but not applied); Phase 15 applies it; MCP tool `search_symbols` exposes it as `[Description]` parameter; `get_references` gets `crossProjectOnly` bool |
</phase_requirements>

---

## Summary

Phase 15 extends the existing BM25 indexing and query stack so that multi-project solution snapshots are queryable with project attribution. The domain is entirely within the C#/.NET codebase — no new external libraries are required. All domain types are already in place: `SymbolNode.ProjectOrigin`, `EdgeScope.CrossProject`, `SolutionSnapshot`, and the `projectFilter` parameter stub on `IKnowledgeQueryService.SearchAsync`. Phase 15 wires these together.

The primary work falls into four areas: (1) store `projectName` in each Lucene document so post-search filtering can apply it, (2) apply the `projectFilter` parameter in `KnowledgeQueryService.SearchAsync`, (3) add `crossProjectOnly` to `GetReferencesAsync` with `EdgeScope` filtering, and (4) surface `projectName` on `SearchResultItem` and expose `project` / `crossProjectOnly` parameters in MCP tool schemas.

The existing test infrastructure (xUnit + FluentAssertions + `BM25SearchIndex(RAMDirectory)` + temp `SnapshotStore`) is well-established and directly reusable. No Wave 0 gaps are expected — the pattern for adding new index tests is demonstrated by `SolutionGraphEnrichmentTests.cs` and `GetReferencesAsyncTests.cs`.

**Primary recommendation:** Store `projectName` as a stored `StringField` in each Lucene document; apply filtering post-search in `KnowledgeQueryService` using `SymbolNode.ProjectOrigin` (already on `_nodes` dictionary); add `ProjectName` to `SearchResultItem`; add `crossProjectOnly` to `GetReferencesAsync`; expose both in MCP tool schema.

---

## Standard Stack

### Core (already in use — no new packages)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Lucene.Net | 4.8.x | BM25 full-text index | Already wired; `BM25SearchIndex` owns all Lucene interaction |
| xUnit | 2.x | Test framework | Project standard per CLAUDE.md |
| FluentAssertions | 6.x | Assertion library | Project standard per CLAUDE.md |
| DocAgent.Core types | in-repo | `SymbolNode`, `SymbolEdge`, `EdgeScope`, `SearchResultItem` | All domain types already defined |

No new NuGet packages are needed for Phase 15.

---

## Architecture Patterns

### Current State (important baseline)

```
IKnowledgeQueryService.SearchAsync(query, kindFilter, offset, limit, snapshotVersion, projectFilter, ct)
  └── projectFilter accepted but NOT applied (stub since Phase 13)

BM25SearchIndex.WriteDocuments()
  └── stores: symbolId, symbolName, fullyQualifiedName, docText
  └── MISSING: projectName field

SymbolNode.ProjectOrigin  ← string?, populated by SolutionIngestionService
BM25SearchIndex._nodes    ← Dictionary<SymbolId, SymbolNode> (populated at IndexAsync time)

IKnowledgeQueryService.GetReferencesAsync(id, ct)
  └── returns all edges bidirectionally where edge.From==id || edge.To==id
  └── MISSING: crossProjectOnly filter, FromProject/ToProject in output
```

### Pattern 1: Store ProjectName in Lucene Document

**What:** Add a stored `StringField` named `"projectName"` to each Lucene document in `WriteDocuments()`.
**When to use:** At index write time — `node.ProjectOrigin ?? string.Empty`.
**Why stored:** Needed for retrieval in search results to avoid a second `_nodes` lookup per hit.

```csharp
// In BM25SearchIndex.WriteDocuments()
private static void WriteDocuments(IndexWriter writer, SymbolGraphSnapshot snapshot)
{
    foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
    {
        var doc = new Document
        {
            new StringField("symbolId",           node.Id.Value,                              Field.Store.YES),
            new TextField  ("symbolName",         node.DisplayName          ?? string.Empty,  Field.Store.YES),
            new TextField  ("fullyQualifiedName", node.FullyQualifiedName   ?? string.Empty,  Field.Store.NO),
            new TextField  ("docText",            node.Docs?.Summary        ?? string.Empty,  Field.Store.NO),
            // NEW: store project name for attribution and filtering
            new StringField("projectName",        node.ProjectOrigin        ?? string.Empty,  Field.Store.YES),
        };
        writer.AddDocument(doc);
    }
}
```

**Source:** Lucene.Net StringField with `Field.Store.YES` pattern — already used for `symbolId` in the same method.

### Pattern 2: Apply projectFilter in KnowledgeQueryService.SearchAsync

**What:** After collecting `SearchHit`s from `_index.SearchAsync()`, filter by `node.ProjectOrigin` when `projectFilter` is non-null.
**Key fact:** `KnowledgeQueryService` already fetches `node` via `_index.GetAsync(hit.Id, ct)` for every hit. `SymbolNode.ProjectOrigin` is already available on the retrieved node.

```csharp
// In KnowledgeQueryService.SearchAsync — existing loop, add one filter line:
await foreach (var hit in _index.SearchAsync(query, ct).ConfigureAwait(false))
{
    ct.ThrowIfCancellationRequested();
    var node = await _index.GetAsync(hit.Id, ct).ConfigureAwait(false);
    if (node is null) continue;
    if (kindFilter.HasValue && node.Kind != kindFilter.Value) continue;
    // NEW: project filter (exact match, case-sensitive; empty filter = all projects)
    if (projectFilter is not null && node.ProjectOrigin != projectFilter) continue;
    filtered.Add(new SearchResultItem(hit.Id, hit.Score, hit.Snippet, node.Kind, node.DisplayName));
}
```

**Backward compat:** When `projectFilter` is `null` (the default), the filter is skipped — all projects returned. Existing callers unchanged.

### Pattern 3: Add ProjectName to SearchResultItem

**What:** Add `string? ProjectName` property to `SearchResultItem` in `QueryTypes.cs`.
**Approach:** Extend the existing positional record with a new optional property.

```csharp
// In DocAgent.Core/QueryTypes.cs
/// <summary>A ranked search result item.</summary>
public sealed record SearchResultItem(
    SymbolId Id,
    double Score,
    string Snippet,
    SymbolKind Kind,
    string DisplayName,
    string? ProjectName = null);   // NEW — null for single-project snapshots
```

**Backward compat:** Default `null` preserves all existing callers using positional construction.

**Constructor call update in KnowledgeQueryService:**
```csharp
filtered.Add(new SearchResultItem(hit.Id, hit.Score, hit.Snippet, node.Kind, node.DisplayName,
    ProjectName: node.ProjectOrigin));
```

### Pattern 4: crossProjectOnly in GetReferencesAsync

**What:** Add `bool crossProjectOnly = false` parameter to `IKnowledgeQueryService.GetReferencesAsync`. When `true`, yield only edges where `edge.Scope == EdgeScope.CrossProject`. Include FromProject / ToProject in serialized output.

**Interface change:**
```csharp
// In DocAgent.Core/Abstractions.cs
IAsyncEnumerable<SymbolEdge> GetReferencesAsync(
    SymbolId id,
    bool crossProjectOnly = false,
    CancellationToken ct = default);
```

**Implementation in KnowledgeQueryService:**
```csharp
public async IAsyncEnumerable<SymbolEdge> GetReferencesAsync(
    SymbolId id,
    bool crossProjectOnly = false,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var (snapshot, _, error) = await ResolveSnapshotAsync(null, ct).ConfigureAwait(false);
    if (error is not null || snapshot is null)
        yield break;

    bool symbolExists = snapshot.Nodes.Any(n => n.Id == id);
    if (!symbolExists)
        throw new SymbolNotFoundException(id);

    foreach (var edge in snapshot.Edges)
    {
        ct.ThrowIfCancellationRequested();
        if (edge.From != id && edge.To != id)
            continue;
        if (crossProjectOnly && edge.Scope != EdgeScope.CrossProject)
            continue;
        yield return edge;
    }
}
```

**Backward compat:** `crossProjectOnly = false` default — existing callers see identical behavior.

### Pattern 5: Ambiguous FQN Resolution Error

**What:** When `get_symbol` is called with a FQN string that maps to multiple symbols across different projects, return an error listing the conflicting projects.
**Context:** `GetSymbolAsync` currently takes `SymbolId` (not FQN string). The MCP tool `get_symbol` passes `symbolId` directly. The ambiguity problem only surfaces if the caller provides a FQN as the symbol ID and two projects produce the same FQN — in practice, SymbolIds are scoped by project (include project prefix from Roslyn assembly-qualified names), so true collisions are rare. The decision states: "return error listing which projects have the symbol."

**Recommended approach:** Add a `GetSymbolByFqnAsync` method to `IKnowledgeQueryService` or handle disambiguation in the MCP tool layer by searching nodes for matching FQN across the snapshot. Since the interface already has `GetSymbolAsync(SymbolId id)`, and SymbolIds are already disambiguated, the ambiguity scenario is: user passes a bare FQN and the tool must resolve it. This is a tool-layer concern.

**Simplest implementation:** In `DocTools.GetSymbol`, if the provided `symbolId` string fails `GetAsync`, attempt FQN lookup across `snapshot.Nodes` and if multiple matches with different `ProjectOrigin` are found, return an error enumerating the project names.

**Note:** The existing `GetSymbolAsync` on the service takes `SymbolId` (opaque string). If the SymbolId encoding already embeds project scope (e.g., `"ProjectA::T:MyNamespace.MyClass"`), collision is structurally prevented. Verify SymbolId encoding in existing tests before deciding if extra disambiguation code is needed.

### Pattern 6: MCP Tool Schema Extension

**What:** Add `project` parameter to `search_symbols` tool; add `crossProjectOnly` to `get_references` tool; include `projectName` in JSON response for both.

```csharp
// search_symbols — add parameter:
[Description("Optional project name filter (exact match, case-sensitive). Omit for all projects.")]
string? project = null,

// Call site update:
var result = await _query.SearchAsync(query, kind, offset, Math.Min(limit, 100),
    projectFilter: project,
    ct: cancellationToken);

// JSON response — include projectName per result:
results = sanitizedItems.Select(i => new {
    id = i.Id.Value, score = i.Score, kind = i.Kind.ToString(),
    displayName = i.DisplayName, snippet = i.Snippet,
    projectName = i.ProjectName   // NEW
}).ToList()
```

```csharp
// get_references — add parameter:
[Description("When true, returns only cross-project edges (EdgeScope.CrossProject).")]
bool crossProjectOnly = false,

// Call site update:
await foreach (var edge in _query.GetReferencesAsync(id, crossProjectOnly, cancellationToken))
    edges.Add(edge);

// JSON response — include project names per edge:
references = edges.Select(e => new {
    fromId = e.From.Value,
    toId = e.To.Value,
    edgeKind = e.Kind.ToString(),
    scope = e.Scope.ToString(),   // NEW: "IntraProject" / "CrossProject" / "External"
}).ToList()
```

### Recommended Change Sequence (Wave Order)

1. **Wave 1 — Core types:** Add `ProjectName` to `SearchResultItem`; add `crossProjectOnly` param to `IKnowledgeQueryService.GetReferencesAsync`.
2. **Wave 2 — Index layer:** Store `projectName` field in `BM25SearchIndex.WriteDocuments()`; no change needed for `InMemorySearchIndex` (uses `_nodes` dict, `ProjectOrigin` already available).
3. **Wave 3 — Service layer:** Apply `projectFilter` in `KnowledgeQueryService.SearchAsync`; apply `crossProjectOnly` in `GetReferencesAsync`.
4. **Wave 4 — MCP tool layer:** Expose `project` param on `search_symbols`; expose `crossProjectOnly` on `get_references`; include `projectName` / `scope` in JSON output.
5. **Wave 5 — Tests:** Unit tests for each behavior; backward compat verification.

### Anti-Patterns to Avoid

- **Querying Lucene by projectName field:** Adding a Lucene `TermQuery` filter for `projectName` at search time is possible but adds complexity. The simpler pattern — filter in `KnowledgeQueryService` post-search using `_nodes` — is already the established pattern for `kindFilter`. Use the same approach.
- **Merging snapshots:** Out of scope and explicitly listed in REQUIREMENTS.md as "Out of Scope: One merged flat SymbolGraphSnapshot."
- **Adding a new `ISearchIndex` overload for project-aware search:** The `ISearchIndex` interface is intentionally thin. Project attribution belongs in the service layer, not the index abstraction.
- **Case-insensitive project name matching:** Locked decision says exact match, case-sensitive. Don't add `StringComparer.OrdinalIgnoreCase`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Stored field retrieval from Lucene doc | Custom doc field registry | `doc.Get("projectName")` — `Field.Store.YES` with `StringField` | Already the pattern for `symbolId` in same file |
| Post-search filtering | Custom index partition | Filter in `KnowledgeQueryService` after `GetAsync` | Consistent with existing `kindFilter` pattern; simple; no Lucene query complexity |
| FQN disambiguation | New lookup table | Scan `snapshot.Nodes` for FQN match at tool layer | Snapshot is in-memory; linear scan over nodes is acceptable for error path |

**Key insight:** The existing index + service architecture already provides the right seams. Phase 15 adds data and wires existing stubs — it does not need new abstractions.

---

## Common Pitfalls

### Pitfall 1: Forgetting to update `SearchAsync` call site with new `SearchResultItem` property
**What goes wrong:** `new SearchResultItem(hit.Id, hit.Score, hit.Snippet, node.Kind, node.DisplayName)` compiles fine with the new optional `ProjectName` — but `ProjectName` is null in results even for solution snapshots where `node.ProjectOrigin` is populated.
**Why it happens:** Optional parameters silently default to null; no compile error.
**How to avoid:** Update the constructor call in `KnowledgeQueryService.SearchAsync` explicitly: `ProjectName: node.ProjectOrigin`.
**Warning signs:** Test asserting `ProjectName != null` for multi-project snapshot fails.

### Pitfall 2: `ISearchIndex` interface change breaks `InMemorySearchIndex`
**What goes wrong:** If `crossProjectOnly` is added to `ISearchIndex.SearchAsync` (wrong layer), `InMemorySearchIndex` must also implement it.
**Why it happens:** Temptation to push filtering into the index abstraction.
**How to avoid:** Keep `crossProjectOnly` only on `IKnowledgeQueryService.GetReferencesAsync`. `ISearchIndex` interface stays unchanged.

### Pitfall 3: Lucene FSDirectory freshness check misses new `projectName` field
**What goes wrong:** An existing persisted Lucene index (built before Phase 15) is reloaded via `LoadIndexAsync` without re-indexing. Old documents lack `projectName` field; all results return `null` project even for solution snapshots.
**Why it happens:** `BM25SearchIndex.IsIndexFresh()` checks `snapshotHash` — same hash, no rebuild.
**How to avoid:** After adding the `projectName` field, the `ContentHash` in the snapshot will differ (since `SolutionIngestionService` recomputes it from nodes). Old persisted indexes won't match any new ingested hash. If backward compat with pre-Phase-15 persisted indexes is needed, document that re-ingestion is required.

### Pitfall 4: SymbolId collision assumption for `get_symbol`
**What goes wrong:** Assuming SymbolIds are project-scoped when they may not be for older single-project snapshots. Querying a v1.0 snapshot by SymbolId works; querying a v1.2 multi-project snapshot may require FQN disambiguation.
**How to avoid:** Check if `SymbolId.Value` in existing tests includes project prefix. If it does not (Roslyn uses `T:Namespace.Type` format without project prefix), two projects with the same type will produce the same `SymbolId` — disambiguation is then required at the service/tool layer, not preventable structurally.

**Verified:** `GetReferencesAsyncTests.cs` uses `new SymbolId("T:A")` — standard Roslyn XML-doc ID format, no project prefix. Conclusion: SymbolIds are NOT project-scoped. The ambiguous FQN scenario is real and the error path is needed.

### Pitfall 5: `snapshot.Edges` vs `SolutionSnapshot` edges
**What goes wrong:** `GetReferencesAsync` currently uses `snapshot.Edges` from a single `SymbolGraphSnapshot`. After Phase 14.1, solution ingestion produces a flat merged snapshot (STATE.md: "Single flat snapshot model preserved") with all edges including CrossProject edges. So `snapshot.Edges` already contains cross-project edges — no need to join across multiple sub-snapshots.
**How to avoid:** Confirm in `SolutionIngestionService` that merged snapshot edges include `EdgeScope.CrossProject` entries. If yes, `crossProjectOnly` filter is simply `edge.Scope == EdgeScope.CrossProject` — straightforward.

---

## Code Examples

### Adding projectName to Lucene document (BM25SearchIndex)

```csharp
// Source: existing BM25SearchIndex.WriteDocuments() pattern; extends with new field
new StringField("projectName", node.ProjectOrigin ?? string.Empty, Field.Store.YES),
```

### Retrieving projectName in SearchAsync result

```csharp
// In BM25SearchIndex.SearchAsync — doc.Get() for stored field
var projectName = doc.Get("projectName");   // empty string for legacy/single-project
yield return new SearchHit(new SymbolId(id), scoreDoc.Score, name);
// Note: ProjectName on SearchResultItem is populated in KnowledgeQueryService, not here
```

### Applying projectFilter in KnowledgeQueryService

```csharp
// projectFilter: exact match, case-sensitive; null means all projects
if (projectFilter is not null && node.ProjectOrigin != projectFilter)
    continue;
```

### crossProjectOnly filter in GetReferencesAsync

```csharp
if (crossProjectOnly && edge.Scope != EdgeScope.CrossProject)
    continue;
```

### Test helper pattern (matches existing SolutionGraphEnrichmentTests style)

```csharp
private static SymbolNode MakeNodeWithProject(string id, string displayName, string project) =>
    new(
        Id:                 new SymbolId(id),
        Kind:               SymbolKind.Type,
        DisplayName:        displayName,
        FullyQualifiedName: displayName,
        PreviousIds:        [],
        Accessibility:      Accessibility.Public,
        Docs:               null,
        Span:               null,
        ReturnType:         null,
        Parameters:         Array.Empty<ParameterInfo>(),
        GenericConstraints: Array.Empty<GenericConstraint>(),
        ProjectOrigin:      project,
        NodeKind:           NodeKind.Real);

private static SymbolEdge CrossProjectEdge(string from, string to) =>
    new(new SymbolId(from), new SymbolId(to), SymbolEdgeKind.Calls, EdgeScope.CrossProject);
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single-project snapshot only | Merged flat snapshot with `ProjectOrigin` per node | Phase 14.1 | `ProjectOrigin` now populated on all nodes from solution ingestion |
| `projectFilter` stub (accepted, not applied) | Must be applied | Phase 15 | Core of this phase's work |
| No `EdgeScope` filtering in `GetReferencesAsync` | `crossProjectOnly` bool added | Phase 15 | Enables "who across project boundaries calls this?" queries |
| `SearchResultItem` without project attribution | `ProjectName` property added | Phase 15 | Callers can see which project each result came from |

---

## Open Questions

1. **SymbolId uniqueness across projects**
   - What we know: SymbolIds use Roslyn XML-doc ID format (`T:Namespace.Type`) without project prefix. Two projects with the same type produce the same SymbolId.
   - What's unclear: Does the current `SolutionIngestionService` deduplicate nodes by SymbolId (last-write-wins) or does it preserve duplicates?
   - Recommendation: Read `SolutionIngestionService.cs` lines 50+ to confirm. If duplicates are preserved in `snapshot.Nodes`, the FQN disambiguation error path is straightforward — scan nodes for FQN and group by ProjectOrigin. If deduplicated, the error path is unreachable in practice.

2. **Lucene FSDirectory backward compat for persisted indexes**
   - What we know: Adding `projectName` to Lucene documents means pre-Phase-15 persisted indexes lack the field.
   - What's unclear: Are there long-lived persisted indexes in use that would be silently broken?
   - Recommendation: Document in release notes that re-ingestion is required after Phase 15. The `IsIndexFresh` hash check will naturally force rebuild when content changes.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.x + FluentAssertions |
| Config file | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| Quick run command | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexing"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TOOLS-01 | `search_symbols` returns results from all projects with `ProjectName` set | unit | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests"` | No — Wave 0 |
| TOOLS-01 | `search_symbols` with `projectFilter` returns only symbols from that project | unit | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests"` | No — Wave 0 |
| TOOLS-01 | `projectFilter` with unknown project name returns empty (not error) | unit | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests"` | No — Wave 0 |
| TOOLS-02 | `get_symbol` resolves across all projects (SymbolId exists in any project) | unit | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests"` | No — Wave 0 |
| TOOLS-02 | `get_symbol` with ambiguous FQN returns error listing conflicting projects | unit | `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests"` | No — Wave 0 |
| TOOLS-03 | `get_references` returns cross-project edges when `crossProjectOnly=false` | unit | `dotnet test --filter "FullyQualifiedName~CrossProjectQueryTests"` | No — Wave 0 |
| TOOLS-03 | `get_references` with `crossProjectOnly=true` returns only `EdgeScope.CrossProject` edges | unit | `dotnet test --filter "FullyQualifiedName~CrossProjectQueryTests"` | No — Wave 0 |
| TOOLS-03 | Cross-project edge results expose source and target project names in JSON | unit | `dotnet test --filter "FullyQualifiedName~CrossProjectQueryTests"` | No — Wave 0 |
| TOOLS-06 | Existing `search_symbols` calls without `project` param return all-project results | unit (backward compat) | `dotnet test` | Existing tests pass as-is |
| TOOLS-06 | MCP tool schema includes `project` param for `search_symbols` | integration | `dotnet test --filter "FullyQualifiedName~McpToolTests"` | Existing — update needed |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~ProjectAwareIndexingTests|CrossProjectQueryTests"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/ProjectAwareIndexingTests.cs` — covers TOOLS-01, TOOLS-02, TOOLS-06
- [ ] `tests/DocAgent.Tests/CrossProjectQueryTests.cs` — covers TOOLS-03

*(Existing `GetReferencesAsyncTests.cs` can be used as the template for `CrossProjectQueryTests.cs`; existing `SolutionGraphEnrichmentTests.cs` for `ProjectAwareIndexingTests.cs`.)*

---

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection of `BM25SearchIndex.cs`, `InMemorySearchIndex.cs`, `KnowledgeQueryService.cs`, `Abstractions.cs`, `Symbols.cs`, `QueryTypes.cs`, `DocTools.cs` — all patterns verified from source
- `SolutionGraphEnrichmentTests.cs`, `GetReferencesAsyncTests.cs` — test patterns verified from source
- `STATE.md` — locked architectural decisions (flat merged snapshot model)
- `CONTEXT.md` — locked user decisions for this phase

### Secondary (MEDIUM confidence)
- Lucene.Net `Field.Store.YES` pattern for stored fields — confirmed from existing `StringField("symbolId", ..., Field.Store.YES)` in same file

### Tertiary (LOW confidence)
- None

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries, existing codebase fully inspected
- Architecture: HIGH — change locations and patterns identified from source
- Pitfalls: HIGH — derived from concrete code analysis (SymbolId format, Lucene persistence, existing stubs)

**Research date:** 2026-03-01
**Valid until:** 2026-04-01 (stable codebase, 30-day validity)
