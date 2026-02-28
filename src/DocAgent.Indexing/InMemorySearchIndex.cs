using System.Runtime.CompilerServices;
using DocAgent.Core;

namespace DocAgent.Indexing;

public sealed class InMemorySearchIndex : ISearchIndex
{
    private readonly Dictionary<SymbolId, SymbolNode> _nodes = new();

    public Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct, bool forceReindex = false)
    {
        _nodes.Clear();
        foreach (var n in snapshot.Nodes)
            _nodes[n.Id] = n;
        return Task.CompletedTask;
    }

    public Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct)
        => Task.FromResult(_nodes.TryGetValue(id, out var n) ? n : null);

#pragma warning disable CS1998 // Async method lacks 'await' operators
    public async IAsyncEnumerable<SearchHit> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // TODO: replace with BM25/inverted index
        foreach (var n in _nodes.Values)
        {
            if ((n.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || (n.Docs?.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                yield return new SearchHit(n.Id, 1.0, n.DisplayName ?? string.Empty);
            }
        }
    }
#pragma warning restore CS1998
}
