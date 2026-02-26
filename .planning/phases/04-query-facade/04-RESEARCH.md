# Phase 4: Query Facade - Research

**Researched:** 2026-02-26
**Domain:** C# service layer / facade pattern over BM25 search index + snapshot store
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Search behavior**
- Filter by `SymbolKind` — optional parameter, agents often want "find me methods named X"
- Pagination via offset + limit (skip/take pattern), stateless
- Default max results: 20
- Accept optional snapshot version — default to latest, but allow pinning for reproducible queries and diff workflows

**Diff semantics**
- A symbol is "modified" if its signature, accessibility, return type, parameters, OR doc comment changed
- Classification is type-only: Added, Removed, Modified — no severity classification (that's Phase 6 analysis)
- Track renames via PreviousIds — show "renamed from X to Y" instead of "removed X, added Y"
- Diff results include SymbolId + change type + brief summary of what changed (e.g., "return type: void → Task"), not full SymbolNode pairs. Caller uses GetSymbolAsync for full details.

**Error handling**
- Structured result types throughout — no exceptions for expected failures (not found, stale index, missing snapshot)
- QueryResult<T> pattern with success/failure status
- Stale index: return results with a staleness warning flag — results are still useful, caller decides whether to re-index
- GetSymbolAsync with non-existent SymbolId: returns not-found result (consistent QueryResult pattern)
- DiffAsync validates snapshot existence internally — returns structured error if either snapshot is missing, no pre-validation required by caller

**Response shape**
- Search results include BM25 relevance score (already computed, cheap to expose)
- Search results include match context snippet (doc summary excerpt or symbol signature) so agents can decide which result to drill into
- GetSymbolAsync returns the SymbolNode plus navigation hints: parent SymbolId, child SymbolIds, related symbol IDs — agents follow links without loading the whole graph
- Standard response envelope on all facade methods: snapshot version, timestamp, staleness flag, query duration

### Claude's Discretion
- Exact `QueryResult<T>` type design and error code enumeration
- Internal caching strategy (if any)
- How match context snippets are extracted from indexed content
- Navigation hint depth (direct children only, or also siblings)

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| QURY-01 | `IKnowledgeQueryService` facade wired to `ISearchIndex` + `SnapshotStore` | KnowledgeQueryService in DocAgent.Indexing (or new DocAgent.Querying project) takes constructor-injected `ISearchIndex` + `SnapshotStore`; facade pattern is straightforward C# service wiring |
| QURY-02 | `SearchAsync` — ranked symbol search results | Delegates to `BM25SearchIndex.SearchAsync`, applies `SymbolKind` filter from in-memory `_nodes` dict, applies offset+limit pagination, wraps in response envelope |
| QURY-03 | `GetSymbolAsync` — full symbol detail by ID | Looks up `SymbolNode` from index's node dictionary via `ISearchIndex.GetAsync`, builds navigation hints by scanning `SymbolEdge` graph from the loaded snapshot, returns structured `QueryResult<SymbolDetail>` |
| QURY-04 | `DiffAsync` — basic structural diff between snapshots | Loads both snapshots via `SnapshotStore.LoadAsync`, computes symmetric diff on node sets, detects renames via `PreviousIds`, emits `DiffEntry` records (Added/Removed/Modified + change summary string) |
</phase_requirements>

---

## Summary

Phase 4 implements `KnowledgeQueryService`, a concrete class that satisfies `IKnowledgeQueryService` by coordinating `ISearchIndex` (BM25) and `SnapshotStore`. No new libraries are needed — all dependencies are already in the codebase. The primary work is designing the response envelope types, implementing diff logic, and writing thorough unit tests without any MCP server involvement.

The existing `IKnowledgeQueryService` interface in `DocAgent.Core/Abstractions.cs` is a starting point, but it does not yet reflect the richer response types decided in CONTEXT.md (e.g., `QueryResult<T>`, staleness flags, navigation hints, response envelope). The interface will need to be updated or a second richer interface defined — the concrete class can implement both, or the core interface can be evolved directly since no MCP consumers exist yet.

The diff algorithm is the most technically interesting piece. It is essentially set-based: index nodes by `SymbolId` in both snapshots, compute added/removed/common sets, check common nodes for field-level changes, then post-process removed+added pairs to detect renames via `PreviousIds`. This is O(N) in node count and requires no external library.

**Primary recommendation:** Place `KnowledgeQueryService` in `DocAgent.Indexing` (it already depends on `DocAgent.Core` and will coordinate the indexing types). Design `QueryResult<T>` as a discriminated-union-style record with a `Success` flag and `ErrorKind` enum. Keep caching out of scope for Phase 4.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| DocAgent.Core | (project ref) | Domain types: `SymbolNode`, `SymbolEdge`, `IKnowledgeQueryService`, `SnapshotRef`, `GraphDiff` | Already defined; all types needed are here |
| DocAgent.Indexing | (project ref) | `BM25SearchIndex`, `ISearchIndex` | Phase 3 deliverable; the facade wraps this |
| DocAgent.Ingestion | (project ref) | `SnapshotStore`, `SnapshotManifestEntry` | Phase 2 deliverable; needed for LoadAsync |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit + FluentAssertions | Already in tests/ | Unit tests for facade logic | All test cases |
| Lucene.Net RAMDirectory | Already in tests/ | In-memory BM25 index for tests | Avoids filesystem in tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Constructor-injected `SnapshotStore` (concrete) | `ISnapshotStore` interface | No interface exists yet; adding one is overhead; concrete class is fine for Phase 4 (Phase 5 DI wiring will resolve) |
| In-facade diff logic | Third-party diff library | Overkill; the domain is well-bounded records, set math is sufficient |

**No new packages required.** All dependencies are already in the solution.

---

## Architecture Patterns

### Recommended Project Structure

The facade lives in `DocAgent.Indexing` (already depends on both `ISearchIndex` and is paired with `BM25SearchIndex`). New types:

```
src/DocAgent.Indexing/
├── KnowledgeQueryService.cs    # IKnowledgeQueryService implementation
└── QueryResult.cs              # QueryResult<T>, QueryErrorKind, SearchResult, SymbolDetail, DiffEntry, ResponseEnvelope

src/DocAgent.Core/
└── Abstractions.cs             # IKnowledgeQueryService — may need signature updates for richer response types
```

Tests in `tests/DocAgent.Tests/`:
```
tests/DocAgent.Tests/
└── KnowledgeQueryServiceTests.cs   # All four requirement tests
```

### Pattern 1: QueryResult<T> — Discriminated Union Result Type

**What:** A generic result container that encodes success/failure without throwing exceptions for expected failures.

**When to use:** All three facade methods return this. Caller pattern-matches on `Success` and reads `Value` or `Error`.

**Design recommendation (Claude's Discretion):**

```csharp
// Source: Project decision — structured error handling, no exceptions for expected failures
public enum QueryErrorKind
{
    NotFound,
    SnapshotMissing,
    StaleIndex,
    InvalidInput
}

public sealed record QueryResult<T>
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public QueryErrorKind? Error { get; init; }
    public string? ErrorMessage { get; init; }

    public static QueryResult<T> Ok(T value) =>
        new() { Success = true, Value = value };

    public static QueryResult<T> Fail(QueryErrorKind error, string? message = null) =>
        new() { Success = false, Error = error, ErrorMessage = message };
}
```

### Pattern 2: Response Envelope

**What:** A wrapper that adds metadata (snapshot version, timestamp, staleness flag, query duration) to every facade response.

**When to use:** All three methods return `QueryResult<ResponseEnvelope<T>>` where the envelope carries the payload plus metadata.

**Design recommendation:**

```csharp
public sealed record ResponseEnvelope<T>(
    T Payload,
    string SnapshotVersion,       // ContentHash of the snapshot used
    DateTimeOffset Timestamp,
    bool IsStale,                  // Index version doesn't match latest snapshot
    TimeSpan QueryDuration);
```

### Pattern 3: SearchAsync — Delegation + Filter + Paginate

**What:** Delegates to `ISearchIndex.SearchAsync`, applies post-filtering by `SymbolKind` (from in-memory `_nodes` dict already maintained by `BM25SearchIndex`), and applies offset+limit pagination.

**When to use:** QURY-02.

```csharp
// Source: Existing BM25SearchIndex pattern + CONTEXT.md decisions
public async IAsyncEnumerable<...> SearchAsync(
    string query,
    SymbolKind? kindFilter = null,
    int offset = 0,
    int limit = 20,
    string? snapshotVersion = null,
    CancellationToken ct = default)
{
    int skipped = 0, yielded = 0;
    await foreach (var hit in _index.SearchAsync(query, ct))
    {
        if (kindFilter is not null)
        {
            var node = await _index.GetAsync(hit.Id, ct);
            if (node?.Kind != kindFilter) continue;
        }
        if (skipped++ < offset) continue;
        if (yielded++ >= limit) break;
        yield return hit; // wrapped in envelope
    }
}
```

**Note:** `IKnowledgeQueryService.SearchAsync` currently returns `IAsyncEnumerable<SearchHit>` with no parameters beyond query. The interface signature will need updating to add the new parameters, or the concrete class can expose a richer overload for Phase 5 to call directly. Recommend updating the interface since no consumers exist yet.

### Pattern 4: GetSymbolAsync — Lookup + Navigation Hints

**What:** Fetches `SymbolNode` from the index's node dictionary via `ISearchIndex.GetAsync`, then builds navigation hints by scanning the snapshot's `Edges` collection.

**Navigation hint depth (Claude's Discretion):** Direct edges only (Contains edges for children, reverse Contains for parent). Siblings are derivable from parent's children — don't include, keep response focused.

```csharp
// Navigation hint extraction from snapshot edges
var parentId = snapshot.Edges
    .FirstOrDefault(e => e.To == id && e.Kind == SymbolEdgeKind.Contains)?.From;
var childIds = snapshot.Edges
    .Where(e => e.From == id && e.Kind == SymbolEdgeKind.Contains)
    .Select(e => e.To)
    .ToList();
var relatedIds = snapshot.Edges
    .Where(e => (e.From == id || e.To == id) && e.Kind != SymbolEdgeKind.Contains)
    .Select(e => e.From == id ? e.To : e.From)
    .Distinct()
    .ToList();
```

**Note:** The `KnowledgeQueryService` needs access to the loaded snapshot (not just the index) to enumerate edges. The service must hold a reference to the current snapshot or load it on demand via `SnapshotStore`.

### Pattern 5: DiffAsync — Set-Based Structural Diff

**What:** Load both snapshots, compute added/removed/common node sets, detect per-field changes, resolve renames via `PreviousIds`.

**Rename detection algorithm:**
1. `removed` = nodes in A not in B (by SymbolId)
2. `added` = nodes in B not in A (by SymbolId)
3. For each `added` node: check if any of its `PreviousIds` appear in `removed` set
4. If match found → emit `Modified` (renamed from X to Y) + remove from both `removed` and `added` sets
5. Remaining `removed` → emit `Removed`
6. Remaining `added` → emit `Added`
7. For common nodes (in both A and B): compare fields → emit `Modified` with change summary string if any differ

**Fields to compare (from CONTEXT.md):** signature (DisplayName + FullyQualifiedName), Accessibility, Kind, Docs (Summary text). Return type and parameters are encoded in DisplayName/FullyQualifiedName for Roslyn-built symbols.

```csharp
public sealed record DiffEntry(
    SymbolId Id,
    DiffChangeKind ChangeKind,
    string Summary);           // e.g., "return type: void → Task", "renamed from Foo to Bar", "doc changed"

public enum DiffChangeKind { Added, Removed, Modified }
```

### Anti-Patterns to Avoid

- **Loading full SymbolNode pairs in diff results:** CONTEXT.md explicitly prohibits this. Return `SymbolId` + change type + summary string only. Caller uses `GetSymbolAsync` for details.
- **Throwing exceptions for missing snapshots:** Use `QueryResult.Fail(QueryErrorKind.SnapshotMissing)`. Do not propagate exceptions from `SnapshotStore.LoadAsync` returning null.
- **Caching inside the facade (Phase 4):** Out of scope per Claude's Discretion. `BM25SearchIndex` already has the node dictionary; do not add a second cache layer now.
- **Updating `IKnowledgeQueryService` to break existing compile-time tests:** `InterfaceCompilationTests.cs` checks `IKnowledgeQueryService` members. Update the interface carefully — existing members must still compile or the test file must be updated in tandem.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| BM25 search | Custom ranking | Existing `BM25SearchIndex.SearchAsync` | Already tested, tuned (k1=2.0, b=0.5) |
| Snapshot loading | Custom deserialization | `SnapshotStore.LoadAsync` | Already handles MessagePack + hash verification |
| Token-efficient query | Custom tokenizer | `CamelCaseAnalyzer` (existing) | Phase 3 deliverable, handles camelCase and acronyms |
| Symbol node lookup | Dictionary scan | `ISearchIndex.GetAsync` (returns from `_nodes` dict) | O(1) lookup already built into BM25SearchIndex |

**Key insight:** The facade's job is coordination, not computation. All hard problems (BM25 ranking, serialization, node lookup) are already solved in Phases 2–3.

---

## Common Pitfalls

### Pitfall 1: Interface Signature Mismatch

**What goes wrong:** The existing `IKnowledgeQueryService` has `SearchAsync(string query, CancellationToken ct)` returning `IAsyncEnumerable<SearchHit>`. The new decided behavior adds `SymbolKind?`, `offset`, `limit`, `snapshotVersion` parameters. If the interface is updated without updating `InterfaceCompilationTests.cs`, the test file breaks.

**Why it happens:** The interface was defined in Phase 1 before query behavior was decided.

**How to avoid:** Update `IKnowledgeQueryService` interface and `InterfaceCompilationTests.cs` in the same task. Or keep the interface minimal and add the richer overload only on the concrete class (less clean).

**Recommendation:** Update the interface — it is the right place for the contract. Phase 5 (MCP wiring) will depend on whatever signature exists here.

### Pitfall 2: Snapshot Access for Edge Traversal

**What goes wrong:** `ISearchIndex.GetAsync` returns a `SymbolNode` but not the edges. Building navigation hints requires the full `SymbolGraphSnapshot.Edges` collection. The `KnowledgeQueryService` needs a way to access the current snapshot's edges.

**Why it happens:** `BM25SearchIndex` stores only nodes (`_nodes` dict), not edges.

**How to avoid:** `KnowledgeQueryService` must hold the current `SymbolGraphSnapshot` directly, or store edges in a secondary dictionary. Recommended: constructor-inject `SnapshotStore` and `ISearchIndex`, track "current snapshot" as a field loaded when the facade is initialized (or loaded on first query using the latest manifest entry).

**Design decision (Claude's Discretion):** Load the latest snapshot from `SnapshotStore.ListAsync()` + `LoadAsync()` on first use. Cache the snapshot reference (not the index) in a private field. This avoids coupling `BM25SearchIndex` internals to edge data.

### Pitfall 3: DiffAsync with Missing Snapshots

**What goes wrong:** `SnapshotStore.LoadAsync` returns `null` if the content hash doesn't exist. Naively calling `.Nodes` on null throws.

**Why it happens:** Null reference on missing snapshot.

**How to avoid:** Explicitly null-check after each `LoadAsync` call and return `QueryResult.Fail(QueryErrorKind.SnapshotMissing, $"Snapshot '{ref.Id}' not found")`.

### Pitfall 4: Pagination Applied Before Kind Filter

**What goes wrong:** If offset/limit is applied to raw `ISearchIndex.SearchAsync` results before `SymbolKind` filtering, the returned page may have fewer than `limit` results even when more matching results exist.

**Why it happens:** Pagination and filtering are applied in wrong order.

**How to avoid:** Filter first (by `SymbolKind`), then paginate (offset/limit). Since `SearchAsync` yields lazily, accumulate filtered results and stop at `limit`.

### Pitfall 5: GraphDiff vs. Richer DiffEntry Types

**What goes wrong:** The existing `GraphDiff` type in `DocAgent.Core/Abstractions.cs` is `sealed record GraphDiff(IReadOnlyList<string> Findings)` — just a list of strings. CONTEXT.md decisions require structured `DiffEntry` records with `SymbolId`, `DiffChangeKind`, and `Summary`.

**Why it happens:** `GraphDiff` was a placeholder stub from Phase 1.

**How to avoid:** Replace or extend `GraphDiff` in `DocAgent.Core`. Either redefine `GraphDiff` as `sealed record GraphDiff(IReadOnlyList<DiffEntry> Entries)` and add `DiffEntry`/`DiffChangeKind` types, or add them alongside. Update `IKnowledgeQueryService.DiffAsync` return type accordingly.

---

## Code Examples

### QueryResult<T> usage pattern

```csharp
// Returning success
return QueryResult<SymbolDetail>.Ok(detail);

// Returning not-found
return QueryResult<SymbolDetail>.Fail(QueryErrorKind.NotFound, $"Symbol '{id.Value}' not found");

// Caller pattern
var result = await service.GetSymbolAsync(id, ct);
if (!result.Success)
{
    // handle result.Error + result.ErrorMessage
    return;
}
var detail = result.Value!;
```

### Diff detection loop (pseudocode verified against CONTEXT.md decisions)

```csharp
var nodesA = snapshotA.Nodes.ToDictionary(n => n.Id);
var nodesB = snapshotB.Nodes.ToDictionary(n => n.Id);

var removedIds = nodesA.Keys.Except(nodesB.Keys).ToHashSet();
var addedIds   = nodesB.Keys.Except(nodesA.Keys).ToHashSet();
var commonIds  = nodesA.Keys.Intersect(nodesB.Keys);

var entries = new List<DiffEntry>();

// Rename detection
foreach (var addedId in addedIds.ToList())
{
    var addedNode = nodesB[addedId];
    var renamedFromId = addedNode.PreviousIds.FirstOrDefault(prev => removedIds.Contains(prev));
    if (renamedFromId != default)
    {
        entries.Add(new DiffEntry(addedId, DiffChangeKind.Modified,
            $"renamed from {renamedFromId.Value}"));
        removedIds.Remove(renamedFromId);
        addedIds.Remove(addedId);
    }
}

// Removed
foreach (var id in removedIds)
    entries.Add(new DiffEntry(id, DiffChangeKind.Removed, "symbol removed"));

// Added
foreach (var id in addedIds)
    entries.Add(new DiffEntry(id, DiffChangeKind.Added, "symbol added"));

// Modified (field-level)
foreach (var id in commonIds)
{
    var a = nodesA[id]; var b = nodesB[id];
    var changes = new List<string>();
    if (a.DisplayName != b.DisplayName)         changes.Add($"name: {a.DisplayName} → {b.DisplayName}");
    if (a.Accessibility != b.Accessibility)     changes.Add($"accessibility: {a.Accessibility} → {b.Accessibility}");
    if (a.Docs?.Summary != b.Docs?.Summary)     changes.Add("doc changed");
    if (changes.Count > 0)
        entries.Add(new DiffEntry(id, DiffChangeKind.Modified, string.Join("; ", changes)));
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `GraphDiff(IReadOnlyList<string> Findings)` stub | `GraphDiff(IReadOnlyList<DiffEntry> Entries)` with structured `DiffEntry` records | Phase 4 | Enables Phase 5 MCP tool to serialize diff results properly |
| `IKnowledgeQueryService.SearchAsync(string, CancellationToken)` | Add `SymbolKind?`, `int offset`, `int limit`, `string? snapshotVersion` parameters | Phase 4 | Matches CONTEXT.md decisions; interface still lives in DocAgent.Core |

---

## Open Questions

1. **Where does `KnowledgeQueryService` live — `DocAgent.Indexing` or new project?**
   - What we know: `DocAgent.Indexing` already has `BM25SearchIndex` and depends on `DocAgent.Core`. `SnapshotStore` is in `DocAgent.Ingestion`. The facade needs both.
   - What's unclear: Does adding a `DocAgent.Ingestion` project reference to `DocAgent.Indexing` create a circular dependency or violation of layer contracts?
   - Recommendation: Check current project references. If `DocAgent.Indexing` does not currently reference `DocAgent.Ingestion`, the simplest fix is to add that reference. Alternatively, place `KnowledgeQueryService` in a new `DocAgent.Querying` project. The simpler option (add reference) is preferred for Phase 4.

2. **Staleness detection: how does the facade know the index is stale?**
   - What we know: `BM25SearchIndex` exposes `LoadIndexAsync` which throws `InvalidOperationException` if stale. No public "is-stale" query method exists on `ISearchIndex`.
   - What's unclear: Whether to add an `IsStale` property to `BM25SearchIndex` or handle staleness purely at the `ISearchIndex.IndexAsync` / `LoadIndexAsync` call boundary.
   - Recommendation: The facade can compare the latest manifest entry's `ContentHash` against the hash the index was loaded with. Store the loaded `contentHash` in a private field on `KnowledgeQueryService`; compare to latest manifest on each query to set the `IsStale` flag in the envelope. No new interface methods needed.

3. **`IKnowledgeQueryService.GetReferencesAsync` — is this in scope for Phase 4?**
   - What we know: `InterfaceCompilationTests.cs` verifies `GetReferencesAsync` exists on the interface. Phase 4 success criteria only lists `SearchAsync`, `GetSymbolAsync`, `DiffAsync`.
   - What's unclear: Whether `GetReferencesAsync` needs a real implementation or can remain a `throw new NotImplementedException()` stub.
   - Recommendation: Implement it as a stub (`yield break;` or `return AsyncEnumerable.Empty<SymbolEdge>()`) to satisfy the interface. Real implementation is a Phase 5/6 concern (MCPS-03 `get_references`).

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.x + FluentAssertions 6.12.1 |
| Config file | none (convention-based discovery) |
| Quick run command | `dotnet test tests/DocAgent.Tests/DocAgent.Tests.csproj --filter "FullyQualifiedName~KnowledgeQueryService"` |
| Full suite command | `dotnet test tests/DocAgent.Tests/DocAgent.Tests.csproj` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| QURY-01 | `KnowledgeQueryService` implements `IKnowledgeQueryService` and takes `ISearchIndex` + `SnapshotStore` via constructor | unit | `dotnet test --filter "FullyQualifiedName~KnowledgeQueryServiceTests"` | ❌ Wave 0 |
| QURY-02 | `SearchAsync` returns BM25-ranked results, filters by `SymbolKind`, respects offset/limit | unit | `dotnet test --filter "FullyQualifiedName~KnowledgeQueryServiceTests"` | ❌ Wave 0 |
| QURY-03 | `GetSymbolAsync` returns `SymbolNode` + navigation hints by ID; returns not-found result for missing ID | unit | `dotnet test --filter "FullyQualifiedName~KnowledgeQueryServiceTests"` | ❌ Wave 0 |
| QURY-04 | `DiffAsync` returns Added/Removed/Modified entries; handles renames via PreviousIds; returns error for missing snapshot | unit | `dotnet test --filter "FullyQualifiedName~KnowledgeQueryServiceTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/DocAgent.Tests/DocAgent.Tests.csproj --filter "FullyQualifiedName~KnowledgeQueryService"`
- **Per wave merge:** `dotnet test tests/DocAgent.Tests/DocAgent.Tests.csproj`
- **Phase gate:** Full suite green (currently 66 tests) before phase complete

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/KnowledgeQueryServiceTests.cs` — covers QURY-01 through QURY-04
- [ ] `src/DocAgent.Core/Abstractions.cs` update — `IKnowledgeQueryService` signature, `GraphDiff`, `DiffEntry`, `DiffChangeKind` types
- [ ] `src/DocAgent.Indexing/QueryResult.cs` — `QueryResult<T>`, `QueryErrorKind`, `ResponseEnvelope<T>`, `SymbolDetail`, `SearchPage` types
- [ ] `src/DocAgent.Indexing/KnowledgeQueryService.cs` — concrete implementation
- [ ] Verify project reference: `DocAgent.Indexing` → `DocAgent.Ingestion` (for `SnapshotStore`) or place service in new project

---

## Sources

### Primary (HIGH confidence)
- Codebase read: `src/DocAgent.Core/Abstractions.cs` — exact current `IKnowledgeQueryService`, `ISearchIndex`, `GraphDiff` signatures
- Codebase read: `src/DocAgent.Core/Symbols.cs` — `SymbolNode`, `SymbolEdge`, `SymbolEdgeKind`, `PreviousIds` field
- Codebase read: `src/DocAgent.Indexing/BM25SearchIndex.cs` — `_nodes` dict, `GetAsync`, `SearchAsync`, `LoadIndexAsync`, node population pattern
- Codebase read: `src/DocAgent.Ingestion/SnapshotStore.cs` — `LoadAsync`, `ListAsync`, `SaveAsync` signatures
- Codebase read: `tests/DocAgent.Tests/InterfaceCompilationTests.cs` — existing compilation checks that must continue to pass
- `.planning/phases/04-query-facade/04-CONTEXT.md` — all locked decisions

### Secondary (MEDIUM confidence)
- `.planning/STATE.md` — accumulated decisions from Phases 1–3, especially BM25 tuning, two-commit Lucene protocol, node dict pattern
- `.planning/REQUIREMENTS.md` — QURY-01 through QURY-04 requirement text

### Tertiary (LOW confidence)
- None required — domain is entirely internal, no third-party library research needed

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all dependencies already in the codebase, no new packages
- Architecture: HIGH — patterns derived directly from reading existing implementations
- Pitfalls: HIGH — derived from actual code inspection (GraphDiff stub, ISearchIndex lacks edge data, interface signature mismatch)

**Research date:** 2026-02-26
**Valid until:** 2026-04-26 (stable — no external dependencies to go stale)
