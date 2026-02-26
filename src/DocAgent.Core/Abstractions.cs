namespace DocAgent.Core;

public sealed record ProjectLocator(string PathOrUrl, string? Ref = null);

public sealed record ProjectInventory(
    string RootPath,
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> XmlDocFiles);

public interface IProjectSource
{
    Task<ProjectInventory> DiscoverAsync(ProjectLocator locator, CancellationToken ct);
}

public sealed record DocInputSet(IReadOnlyDictionary<string, string> XmlDocsByAssemblyName);

public interface IDocSource
{
    Task<DocInputSet> LoadAsync(ProjectInventory inv, CancellationToken ct);
}

public interface ISymbolGraphBuilder
{
    Task<SymbolGraphSnapshot> BuildAsync(ProjectInventory inv, DocInputSet docs, CancellationToken ct);
}

public sealed record SearchHit(SymbolId Id, double Score, string Snippet);

public interface ISearchIndex
{
    Task IndexAsync(SymbolGraphSnapshot snapshot, CancellationToken ct);
    IAsyncEnumerable<SearchHit> SearchAsync(string query, CancellationToken ct = default);
    Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct);
}

public sealed record SnapshotRef(string Id);

public sealed record GraphDiff(IReadOnlyList<string> Findings);

public interface IKnowledgeQueryService
{
    IAsyncEnumerable<SearchHit> SearchAsync(string query, CancellationToken ct = default);
    Task<SymbolNode?> GetSymbolAsync(SymbolId id, CancellationToken ct);
    Task<GraphDiff> DiffAsync(SnapshotRef a, SnapshotRef b, CancellationToken ct);
    IAsyncEnumerable<SymbolEdge> GetReferencesAsync(SymbolId id, CancellationToken ct = default);
}

/// <summary>
/// V2 vector search index. Intentionally empty in V1 — implementations are a v2 concern (VCTR-01).
/// </summary>
public interface IVectorIndex
{
}

public static class SearchIndexExtensions
{
    public static async Task<IReadOnlyList<SearchHit>> SearchToListAsync(
        this ISearchIndex index, string query, CancellationToken ct = default)
    {
        var results = new List<SearchHit>();
        await foreach (var hit in index.SearchAsync(query, ct))
            results.Add(hit);
        return results;
    }
}
