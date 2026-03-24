using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Enriches a <see cref="SymbolGraphSnapshot"/> with cross-language edges
/// detected between script/tool symbols and C# symbols.
/// </summary>
public static class SnapshotEnricher
{
    /// <summary>
    /// Returns a new snapshot with cross-language edges appended.
    /// Preserves all existing nodes and edges. Deduplicates edges (same From/To/Kind = skip).
    /// </summary>
    public static SymbolGraphSnapshot EnrichWithCrossLanguageEdges(SymbolGraphSnapshot snapshot)
    {
        var newEdges = CrossLanguageEdgeDetector.DetectEdges(snapshot);

        if (newEdges.Count == 0)
            return snapshot;

        // Deduplicate against existing edges
        var existingEdgeSet = new HashSet<(string From, string To, SymbolEdgeKind Kind)>(
            snapshot.Edges.Select(e => (e.From.Value, e.To.Value, e.Kind)));

        var deduplicated = newEdges
            .Where(e => existingEdgeSet.Add((e.From.Value, e.To.Value, e.Kind)))
            .ToList();

        if (deduplicated.Count == 0)
            return snapshot;

        var allEdges = snapshot.Edges.Concat(deduplicated).ToList();

        return snapshot with { Edges = allEdges };
    }
}
