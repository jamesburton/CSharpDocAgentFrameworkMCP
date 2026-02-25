using DocAgent.Core;

namespace DocAgent.Indexing;

public sealed class InMemorySearchIndex : ISearchIndex
{
    private readonly Dictionary<SymbolId, SymbolNode> _nodes = new();

    public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct)
    {
        _nodes.Clear();
        foreach (var n in snapshot.Nodes)
            _nodes[n.Id] = n;
        return Task.CompletedTask;
    }

    public Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct)
        => Task.FromResult(_nodes.TryGetValue(id, out var n) ? n : null);

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct)
    {
        // TODO: replace with BM25/inverted index
        var hits = _nodes.Values
            .Where(n => (n.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                     || (n.Docs?.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(n => new SearchHit(n.Id, 1.0, n.DisplayName))
            .ToList();
        return Task.FromResult((IReadOnlyList<SearchHit>)hits);
    }
}
