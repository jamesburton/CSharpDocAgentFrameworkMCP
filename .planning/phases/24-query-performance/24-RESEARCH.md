# Phase 24 Research: Query Performance

**Phase:** 24 - Query Performance
**Confidence:** HIGH
**Researched:** 2026-03-08

## Summary

All three performance bottlenecks are in a single file (`src/DocAgent.Indexing/KnowledgeQueryService.cs`) and require no interface or domain model changes. The fixes are internal refactoring using standard .NET Dictionary/List collections.

## Bottleneck Analysis

### PERF-01: Symbol Existence Check — O(N) → O(1)

**Location:** `KnowledgeQueryService.cs:228`
```csharp
bool symbolExists = snapshot.Nodes.Any(n => n.Id == id);
```

**Problem:** Linear scan of all nodes to check if a symbol exists. Called on every `GetReferencesAsync` invocation.

**Fix:** Build a `Dictionary<SymbolId, SymbolNode>` from `snapshot.Nodes` at snapshot resolution time. Replace `Any()` with `ContainsKey()`.

**Note:** `BM25SearchIndex.GetAsync` (line 153-154) already uses `_nodes.TryGetValue()` — this pattern is proven in the codebase.

### PERF-02: Edge Traversal — O(E) → O(1)

**Location:** `KnowledgeQueryService.cs:101-117` (GetSymbolAsync) and `233-242` (GetReferencesAsync)

```csharp
// GetSymbolAsync — scans ALL edges
foreach (var edge in snapshot.Edges)
{
    if (edge.Kind == SymbolEdgeKind.Contains) { ... }
    else { ... }
}

// GetReferencesAsync — scans ALL edges
foreach (var edge in snapshot.Edges)
{
    if (edge.From == id || edge.To == id) { ... }
}
```

**Problem:** Both methods scan every edge O(E) to find edges involving a specific symbol. For large graphs, this is the dominant cost.

**Fix:** Build pre-indexed edge dictionaries at snapshot resolution time:
- `Dictionary<SymbolId, List<SymbolEdge>> _edgesByFrom` — edges keyed by `From`
- `Dictionary<SymbolId, List<SymbolEdge>> _edgesByTo` — edges keyed by `To`

GetSymbolAsync: Look up `_edgesByTo[id]` for parent (Contains edges where target is this node), `_edgesByFrom[id]` for children and outgoing relations.
GetReferencesAsync: Union of `_edgesByFrom[id]` and `_edgesByTo[id]`.

### PERF-03: SearchAsync Metadata — Per-Hit Async → Direct Lookup

**Location:** `KnowledgeQueryService.cs:52`
```csharp
var node = await _index.GetAsync(hit.Id, ct).ConfigureAwait(false);
```

**Problem:** Per search hit, calls `_index.GetAsync()` which is an async method even though `BM25SearchIndex.GetAsync` is actually synchronous (`Task.FromResult` over a dictionary lookup). The async machinery overhead is unnecessary.

**Fix:** Build a `Dictionary<SymbolId, SymbolNode>` node map (same one used for PERF-01) and do a direct synchronous dictionary lookup instead of going through the async ISearchIndex.GetAsync path.

This avoids:
1. Async state machine allocation per hit
2. `Task.FromResult` wrapper allocation in BM25SearchIndex
3. Indirection through the interface

## Architecture Approach

### Cached Node/Edge Maps

The best approach is to build lookup structures lazily when a snapshot is resolved, then cache them for the snapshot's lifetime. Since `ResolveSnapshotAsync` already resolves the snapshot, we can build the maps there.

**Option A — Build maps in a helper, cache per content hash:**
```csharp
private sealed class SnapshotLookup
{
    public Dictionary<SymbolId, SymbolNode> NodeById { get; }
    public Dictionary<SymbolId, List<SymbolEdge>> EdgesByFrom { get; }
    public Dictionary<SymbolId, List<SymbolEdge>> EdgesByTo { get; }
}

private SnapshotLookup? _cachedLookup;
private string? _cachedHash;
```

Rebuild only when the snapshot content hash changes. This is the recommended approach — simple, no external dependencies, and the cache invalidates naturally when a new snapshot is loaded.

**Option B — TTL-based cache with IMemoryCache:**
Unnecessary complexity for this use case. The snapshot is versioned by content hash, so hash-based invalidation is sufficient.

### Recommended: Option A

## Determinism Impact

**No determinism risk.** The new dictionaries are internal lookup structures that never feed into serialization. The snapshot's `IReadOnlyList<SymbolNode>` and `IReadOnlyList<SymbolEdge>` remain the serialization source of truth.

Key points:
- `GetSymbolAsync` returns a `SymbolDetail` built from specific lookups — order comes from the edge dictionaries' `List<SymbolEdge>` which preserves insertion order (same as original iteration order)
- `GetReferencesAsync` yields edges — order is determined by list iteration, same as before
- `SearchAsync` returns results ordered by BM25 score, unchanged
- `DiffAsync` already builds its own `ToDictionary()` maps (lines 150-151) — not affected

## Files to Modify

| File | Changes |
|------|---------|
| `src/DocAgent.Indexing/KnowledgeQueryService.cs` | Add SnapshotLookup class, build maps in ResolveSnapshotAsync, replace linear scans |

No new files needed. No interface changes. No domain model changes.

## Test Impact

All 330 existing tests should pass unchanged. The refactoring preserves:
- Same return values and ordering
- Same error semantics (SymbolNotFoundException)
- Same async/streaming behavior for GetReferencesAsync

## Open Questions

None — the scope is well-defined and the implementation path is clear.

## Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Standard Stack | HIGH | Pure internal refactoring using standard .NET Dictionary/List |
| Architecture | HIGH | Pattern already proven in codebase (BM25SearchIndex._nodes) |
| Pitfalls | HIGH | Determinism concerns well-understood; all tests use Contains not Equal for ordering |
| Scope | HIGH | Single file change, no API surface changes |
