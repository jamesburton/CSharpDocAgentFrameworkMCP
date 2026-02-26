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

        // Collect raw hits from the index, filtered and paginated
        var filtered = new List<SearchResultItem>();
        await foreach (var hit in _index.SearchAsync(query, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var node = await _index.GetAsync(hit.Id, ct).ConfigureAwait(false);
            if (node is null)
                continue;
            if (kindFilter.HasValue && node.Kind != kindFilter.Value)
                continue;
            filtered.Add(new SearchResultItem(hit.Id, hit.Score, hit.Snippet, node.Kind, node.DisplayName));
        }

        var page = filtered.Skip(offset).Take(limit).ToList();
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

        // Build navigation hints from snapshot edges
        SymbolId? parentId = null;
        var childIds = new List<SymbolId>();
        var relatedIds = new HashSet<SymbolId>();

        foreach (var edge in snapshot.Edges)
        {
            if (edge.Kind == SymbolEdgeKind.Contains)
            {
                if (edge.To == id)
                    parentId = edge.From;
                else if (edge.From == id)
                    childIds.Add(edge.To);
            }
            else
            {
                if (edge.From == id)
                    relatedIds.Add(edge.To);
                else if (edge.To == id)
                    relatedIds.Add(edge.From);
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
    // GetReferencesAsync (stub — MCPS-03, Phase 5/6 concern)
    // -------------------------------------------------------------------------

    public async IAsyncEnumerable<SymbolEdge> GetReferencesAsync(
        SymbolId id,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
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
}
