using DocAgent.Core;

namespace DocAgent.Ingestion;

/// <summary>
/// Provides deterministic ordering of symbol collections for consistent snapshot output.
/// All collections are sorted by documentation comment ID (Ordinal) to ensure that
/// identical inputs always produce identical output regardless of Roslyn enumeration order.
/// </summary>
public static class SymbolSorter
{
    /// <summary>
    /// Sorts symbol nodes by <see cref="SymbolId.Value"/> using Ordinal string comparison.
    /// </summary>
    public static IReadOnlyList<SymbolNode> SortNodes(IEnumerable<SymbolNode> nodes)
        => nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Sorts symbol edges by From, then To, then Kind for a stable, canonical ordering.
    /// </summary>
    public static IReadOnlyList<SymbolEdge> SortEdges(IEnumerable<SymbolEdge> edges)
        => edges
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ToList();
}
