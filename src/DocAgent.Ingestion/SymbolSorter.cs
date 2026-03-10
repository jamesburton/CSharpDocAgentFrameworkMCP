using System.Runtime.InteropServices;
using DocAgent.Core;

namespace DocAgent.Ingestion;

/// <summary>
/// Provides deterministic ordering of symbol collections for consistent snapshot output.
/// All collections are sorted by documentation comment ID (Ordinal) to ensure that
/// identical inputs always produce identical output regardless of Roslyn enumeration order.
/// </summary>
public static class SymbolSorter
{
    private static readonly Comparer<SymbolEdge> s_edgeComparer = Comparer<SymbolEdge>.Create(
        (a, b) =>
        {
            int c = StringComparer.Ordinal.Compare(a.From.Value, b.From.Value);
            if (c != 0) return c;
            c = StringComparer.Ordinal.Compare(a.To.Value, b.To.Value);
            return c != 0 ? c : a.Kind.CompareTo(b.Kind);
        });

    /// <summary>
    /// Sorts symbol nodes by <see cref="SymbolId.Value"/> using Ordinal string comparison.
    /// </summary>
    public static IReadOnlyList<SymbolNode> SortNodes(IEnumerable<SymbolNode> nodes)
        => nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Sorts symbol nodes in-place (zero allocation) and returns the same list.
    /// </summary>
    public static IReadOnlyList<SymbolNode> SortNodes(List<SymbolNode> nodes)
    {
        MemoryExtensions.Sort(CollectionsMarshal.AsSpan(nodes),
            static (a, b) => StringComparer.Ordinal.Compare(a.Id.Value, b.Id.Value));
        return nodes;
    }

    /// <summary>
    /// Sorts symbol edges by From, then To, then Kind for a stable, canonical ordering.
    /// </summary>
    public static IReadOnlyList<SymbolEdge> SortEdges(IEnumerable<SymbolEdge> edges)
        => edges
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ToList();

    /// <summary>
    /// Sorts symbol edges in-place (zero allocation) and returns the same list.
    /// </summary>
    public static IReadOnlyList<SymbolEdge> SortEdges(List<SymbolEdge> edges)
    {
        MemoryExtensions.Sort(CollectionsMarshal.AsSpan(edges), s_edgeComparer);
        return edges;
    }
}
