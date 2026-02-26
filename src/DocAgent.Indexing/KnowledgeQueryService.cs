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
    // DiffAsync (stub — Phase 5/6 concern)
    // -------------------------------------------------------------------------

    public Task<QueryResult<ResponseEnvelope<GraphDiff>>> DiffAsync(
        SnapshotRef a, SnapshotRef b, CancellationToken ct = default)
        => Task.FromResult(
            QueryResult<ResponseEnvelope<GraphDiff>>.Fail(
                QueryErrorKind.InvalidInput, "DiffAsync is not implemented in V1."));

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
