using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocAgent.Core;
using DocAgent.Ingestion;

namespace DocAgent.Indexing;

/// <summary>
/// Facade that wires ISearchIndex and SnapshotStore together to serve
/// SearchAsync and GetSymbolAsync with ResponseEnvelope metadata.
/// </summary>
public sealed class KnowledgeQueryService : IKnowledgeQueryService
{
    private readonly ISearchIndex _index;
    private readonly SnapshotStore _snapshotStore;
    private SnapshotLookup? _cachedLookup;
    private string? _cachedHash;

    public KnowledgeQueryService(ISearchIndex index, SnapshotStore snapshotStore)
    {
        _index = index;
        _snapshotStore = snapshotStore;
    }

    // -------------------------------------------------------------------------
    // SearchAsync
    // -------------------------------------------------------------------------

    public async Task<QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>> SearchAsync(
        string query,
        SymbolKind? kindFilter = null,
        int offset = 0,
        int limit = 20,
        string? snapshotVersion = null,
        string? projectFilter = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Resolve snapshot
        var (snapshot, latestHash, resolveError) = await ResolveSnapshotAsync(snapshotVersion, ct).ConfigureAwait(false);
        if (resolveError is not null)
            return QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>.Fail(resolveError.Value, "Snapshot could not be resolved.");
        if (snapshot is null || latestHash is null)
            return QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>.Fail(QueryErrorKind.SnapshotMissing);

        var isStale = snapshot.ContentHash != latestHash;
        var lookup = GetOrBuildLookup(snapshot);

        // Collect raw hits from the index, filtered and paginated.
        // projectFilter is pushed to the index for Lucene-level scoping (prevents
        // framework types from dominating the top-N cutoff).
        var filtered = new List<SearchResultItem>();
        var skipRemaining = offset;
        var taken = 0;
        await foreach (var hit in _index.SearchAsync(query, ct, projectFilter).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            lookup.NodeById.TryGetValue(hit.Id, out var node);
            if (node is null)
                continue;
            if (kindFilter.HasValue && node.Kind != kindFilter.Value)
                continue;

            // Skip items before offset, then collect up to limit.
            if (skipRemaining > 0)
            {
                skipRemaining--;
                continue;
            }
            filtered.Add(new SearchResultItem(hit.Id, hit.Score, hit.Snippet, node.Kind, node.DisplayName, ProjectName: node.ProjectOrigin));
            if (++taken >= limit)
                break;
        }

        var page = filtered;
        sw.Stop();

        var envelope = new ResponseEnvelope<IReadOnlyList<SearchResultItem>>(
            Payload: page,
            SnapshotVersion: snapshot.ContentHash ?? string.Empty,
            Timestamp: DateTimeOffset.UtcNow,
            IsStale: isStale,
            QueryDuration: sw.Elapsed);

        return QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>.Ok(envelope);
    }

    // -------------------------------------------------------------------------
    // GetSymbolAsync
    // -------------------------------------------------------------------------

    public async Task<QueryResult<ResponseEnvelope<SymbolDetail>>> GetSymbolAsync(
        SymbolId id,
        string? snapshotVersion = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var (snapshot, latestHash, resolveError) = await ResolveSnapshotAsync(snapshotVersion, ct).ConfigureAwait(false);
        if (resolveError is not null)
            return QueryResult<ResponseEnvelope<SymbolDetail>>.Fail(resolveError.Value, "Snapshot could not be resolved.");
        if (snapshot is null || latestHash is null)
            return QueryResult<ResponseEnvelope<SymbolDetail>>.Fail(QueryErrorKind.SnapshotMissing);

        var node = await _index.GetAsync(id, ct).ConfigureAwait(false);
        if (node is null)
            return QueryResult<ResponseEnvelope<SymbolDetail>>.Fail(QueryErrorKind.NotFound, $"Symbol '{id.Value}' not found.");

        var lookup = GetOrBuildLookup(snapshot);

        // Build navigation hints from indexed edges
        SymbolId? parentId = null;
        var childIds = new List<SymbolId>();
        var relatedIds = new HashSet<SymbolId>();

        // Edges where this symbol is the target (someone points TO us)
        if (lookup.EdgesByTo.TryGetValue(id, out var incomingEdges))
        {
            foreach (var edge in incomingEdges)
            {
                if (edge.Kind == SymbolEdgeKind.Contains)
                    parentId = edge.From;
                else
                    relatedIds.Add(edge.From);
            }
        }

        // Edges where this symbol is the source (we point FROM)
        if (lookup.EdgesByFrom.TryGetValue(id, out var outgoingEdges))
        {
            foreach (var edge in outgoingEdges)
            {
                if (edge.Kind == SymbolEdgeKind.Contains)
                    childIds.Add(edge.To);
                else
                    relatedIds.Add(edge.To);
            }
        }

        var isStale = snapshot.ContentHash != latestHash;
        sw.Stop();

        var detail = new SymbolDetail(node, parentId, childIds, relatedIds.ToList());
        var envelope = new ResponseEnvelope<SymbolDetail>(
            Payload: detail,
            SnapshotVersion: snapshot.ContentHash ?? string.Empty,
            Timestamp: DateTimeOffset.UtcNow,
            IsStale: isStale,
            QueryDuration: sw.Elapsed);

        return QueryResult<ResponseEnvelope<SymbolDetail>>.Ok(envelope);
    }

    // -------------------------------------------------------------------------
    // DiffAsync
    // -------------------------------------------------------------------------

    public async Task<QueryResult<ResponseEnvelope<GraphDiff>>> DiffAsync(
        SnapshotRef a, SnapshotRef b, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var snapshotA = await _snapshotStore.LoadAsync(a.Id, ct).ConfigureAwait(false);
        if (snapshotA is null)
            return QueryResult<ResponseEnvelope<GraphDiff>>.Fail(QueryErrorKind.SnapshotMissing, $"Snapshot '{a.Id}' not found.");

        var snapshotB = await _snapshotStore.LoadAsync(b.Id, ct).ConfigureAwait(false);
        if (snapshotB is null)
            return QueryResult<ResponseEnvelope<GraphDiff>>.Fail(QueryErrorKind.SnapshotMissing, $"Snapshot '{b.Id}' not found.");

        var nodesA = snapshotA.Nodes.ToDictionary(n => n.Id);
        var nodesB = snapshotB.Nodes.ToDictionary(n => n.Id);

        var removedIds = nodesA.Keys.Except(nodesB.Keys).ToHashSet();
        var addedIds = nodesB.Keys.Except(nodesA.Keys).ToHashSet();
        var commonIds = nodesA.Keys.Intersect(nodesB.Keys).ToList();

        var entries = new List<DiffEntry>();

        // Rename detection via PreviousIds
        foreach (var id in addedIds.ToList())
        {
            var prevId = nodesB[id].PreviousIds.FirstOrDefault(p => removedIds.Contains(p));
            if (prevId != default)
            {
                entries.Add(new DiffEntry(id, DiffChangeKind.Modified, $"renamed from {prevId.Value}"));
                removedIds.Remove(prevId);
                addedIds.Remove(id);
            }
        }

        // Removed
        foreach (var id in removedIds)
            entries.Add(new DiffEntry(id, DiffChangeKind.Removed, "symbol removed"));

        // Added
        foreach (var id in addedIds)
            entries.Add(new DiffEntry(id, DiffChangeKind.Added, "symbol added"));

        // Modified (common symbols with changes)
        foreach (var id in commonIds)
        {
            var na = nodesA[id];
            var nb = nodesB[id];
            var changes = new List<string>();

            if (na.DisplayName != nb.DisplayName)
                changes.Add($"name: {na.DisplayName} \u2192 {nb.DisplayName}");
            if (na.FullyQualifiedName != nb.FullyQualifiedName)
                changes.Add($"fqn: {na.FullyQualifiedName} \u2192 {nb.FullyQualifiedName}");
            if (na.Accessibility != nb.Accessibility)
                changes.Add($"accessibility: {na.Accessibility} \u2192 {nb.Accessibility}");
            if (na.Kind != nb.Kind)
                changes.Add($"kind: {na.Kind} \u2192 {nb.Kind}");
            if (na.Docs?.Summary != nb.Docs?.Summary)
                changes.Add("doc changed");

            if (changes.Count > 0)
                entries.Add(new DiffEntry(id, DiffChangeKind.Modified, string.Join("; ", changes)));
        }

        sw.Stop();

        var diff = new GraphDiff(entries);
        var envelope = new ResponseEnvelope<GraphDiff>(
            Payload: diff,
            SnapshotVersion: snapshotB.ContentHash ?? string.Empty,
            Timestamp: DateTimeOffset.UtcNow,
            IsStale: false,
            QueryDuration: sw.Elapsed);

        return QueryResult<ResponseEnvelope<GraphDiff>>.Ok(envelope);
    }

    // -------------------------------------------------------------------------
    // GetReferencesAsync
    // -------------------------------------------------------------------------

    public async IAsyncEnumerable<SymbolEdge> GetReferencesAsync(
        SymbolId id,
        bool crossProjectOnly = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (snapshot, _, error) = await ResolveSnapshotAsync(null, ct).ConfigureAwait(false);
        if (error is not null || snapshot is null)
            yield break;

        var lookup = GetOrBuildLookup(snapshot);

        // Verify symbol exists in the graph — throw if not found
        bool symbolExists = lookup.NodeById.ContainsKey(id);
        if (!symbolExists)
            throw new SymbolNotFoundException(id);

        // Bidirectional: return edges where symbol appears at either end
        if (lookup.EdgesByFrom.TryGetValue(id, out var fromEdges))
        {
            foreach (var edge in fromEdges)
            {
                ct.ThrowIfCancellationRequested();
                if (crossProjectOnly && edge.Scope != EdgeScope.CrossProject)
                    continue;
                yield return edge;
            }
        }

        if (lookup.EdgesByTo.TryGetValue(id, out var toEdges))
        {
            foreach (var edge in toEdges)
            {
                ct.ThrowIfCancellationRequested();
                if (crossProjectOnly && edge.Scope != EdgeScope.CrossProject)
                    continue;
                yield return edge;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the resolved snapshot, the latest known hash, and an optional error kind.
    /// When snapshotVersion is null, resolves to the most recent snapshot by CreatedAt.
    /// </summary>
    private async Task<(SymbolGraphSnapshot? snapshot, string? latestHash, QueryErrorKind? error)>
        ResolveSnapshotAsync(string? snapshotVersion, CancellationToken ct)
    {
        var manifest = await _snapshotStore.ListAsync(ct).ConfigureAwait(false);
        if (manifest.Count == 0)
            return (null, null, QueryErrorKind.SnapshotMissing);

        // Latest entry by IngestedAt
        var latestEntry = manifest.OrderByDescending(e => e.IngestedAt).First();
        var latestHash = latestEntry.ContentHash;

        var targetHash = snapshotVersion ?? latestHash;
        var snapshot = await _snapshotStore.LoadAsync(targetHash, ct).ConfigureAwait(false);
        if (snapshot is null)
            return (null, latestHash, QueryErrorKind.SnapshotMissing);

        return (snapshot, latestHash, null);
    }

    /// <summary>
    /// Returns a cached <see cref="SnapshotLookup"/> for the given snapshot,
    /// rebuilding only when the content hash changes.
    /// </summary>
    private SnapshotLookup GetOrBuildLookup(SymbolGraphSnapshot snapshot)
    {
        var hash = snapshot.ContentHash;
        if (_cachedLookup is not null && _cachedHash == hash)
            return _cachedLookup;

        var lookup = new SnapshotLookup(snapshot);
        _cachedLookup = lookup;
        _cachedHash = hash;
        return lookup;
    }

    // -------------------------------------------------------------------------
    // SnapshotLookup (private nested class)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pre-built dictionaries for O(1) node and edge lookups from a snapshot.
    /// Replaces linear scans over <see cref="SymbolGraphSnapshot.Nodes"/> and
    /// <see cref="SymbolGraphSnapshot.Edges"/> in query methods.
    /// </summary>
    private sealed class SnapshotLookup
    {
        public Dictionary<SymbolId, SymbolNode> NodeById { get; }
        public Dictionary<SymbolId, List<SymbolEdge>> EdgesByFrom { get; }
        public Dictionary<SymbolId, List<SymbolEdge>> EdgesByTo { get; }

        public SnapshotLookup(SymbolGraphSnapshot snapshot)
        {
            NodeById = new Dictionary<SymbolId, SymbolNode>(snapshot.Nodes.Count);
            foreach (var node in snapshot.Nodes)
                NodeById[node.Id] = node;

            EdgesByFrom = new Dictionary<SymbolId, List<SymbolEdge>>();
            EdgesByTo = new Dictionary<SymbolId, List<SymbolEdge>>();

            foreach (var edge in snapshot.Edges)
            {
                if (!EdgesByFrom.TryGetValue(edge.From, out var fromList))
                {
                    fromList = new List<SymbolEdge>();
                    EdgesByFrom[edge.From] = fromList;
                }
                fromList.Add(edge);

                if (!EdgesByTo.TryGetValue(edge.To, out var toList))
                {
                    toList = new List<SymbolEdge>();
                    EdgesByTo[edge.To] = toList;
                }
                toList.Add(edge);
            }
        }
    }
}
