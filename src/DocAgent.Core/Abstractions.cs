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
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
    Task<SymbolNode?> GetAsync(SymbolId id, CancellationToken ct);
}

public sealed record SnapshotRef(string Id);

public sealed record GraphDiff(IReadOnlyList<string> Findings);

public interface IKnowledgeQueryService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct);
    Task<SymbolNode?> GetSymbolAsync(SymbolId id, CancellationToken ct);
    Task<GraphDiff> DiffAsync(SnapshotRef a, SnapshotRef b, CancellationToken ct);
}
