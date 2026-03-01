using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

/// <summary>
/// Tests for cross-project edge filtering via GetReferencesAsync(crossProjectOnly).
/// Phase 15 — TOOLS-01, TOOLS-03, TOOLS-06.
/// </summary>
[Trait("Category", "Unit")]
public class CrossProjectQueryTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ---------- helpers -------------------------------------------------------

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CrossProjectQueryTests_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static SymbolNode MakeNode(string id, string? project = null) =>
        new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: id,
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            ProjectOrigin: project,
            NodeKind: NodeKind.Real);

    private static SymbolEdge MakeCrossEdge(string from, string to) =>
        new(new SymbolId(from), new SymbolId(to), SymbolEdgeKind.Calls, EdgeScope.CrossProject);

    private static SymbolEdge MakeIntraEdge(string from, string to) =>
        new(new SymbolId(from), new SymbolId(to), SymbolEdgeKind.Calls, EdgeScope.IntraProject);

    private static SymbolGraphSnapshot BuildSnapshot(SymbolNode[] nodes, SymbolEdge[] edges) =>
        new(
            SchemaVersion: "v1",
            ProjectName: "TestProject",
            SourceFingerprint: "fp1",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: edges);

    private async Task<KnowledgeQueryService> BuildService(SymbolNode[] nodes, SymbolEdge[] edges)
    {
        var snapshot = BuildSnapshot(nodes, edges);
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var saved = await store.SaveAsync(snapshot);

        var index = new BM25SearchIndex(new RAMDirectory());
        await index.IndexAsync(saved, CancellationToken.None);

        return new KnowledgeQueryService(index, store);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------- tests ---------------------------------------------------------

    [Fact]
    public async Task GetReferencesAsync_CrossProjectOnlyFalse_ReturnsAllEdges()
    {
        var a = MakeNode("T:A", "ProjectA");
        var b = MakeNode("T:B", "ProjectA");
        var c = MakeNode("T:C", "ProjectB");
        var intra = MakeIntraEdge("T:A", "T:B");
        var cross = MakeCrossEdge("T:A", "T:C");
        var sut = await BuildService([a, b, c], [intra, cross]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A"), crossProjectOnly: false))
            results.Add(e);

        results.Should().HaveCount(2);
        results.Should().Contain(intra);
        results.Should().Contain(cross);
    }

    [Fact]
    public async Task GetReferencesAsync_CrossProjectOnlyTrue_ReturnsOnlyCrossProjectEdges()
    {
        var a = MakeNode("T:A", "ProjectA");
        var b = MakeNode("T:B", "ProjectA");
        var c = MakeNode("T:C", "ProjectB");
        var intra = MakeIntraEdge("T:A", "T:B");
        var cross = MakeCrossEdge("T:A", "T:C");
        var sut = await BuildService([a, b, c], [intra, cross]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A"), crossProjectOnly: true))
            results.Add(e);

        results.Should().ContainSingle().Which.Should().Be(cross);
        results.Should().AllSatisfy(e => e.Scope.Should().Be(EdgeScope.CrossProject));
    }

    [Fact]
    public async Task GetReferencesAsync_CrossProjectOnlyTrue_NoMatchingEdges_ReturnsEmpty()
    {
        var a = MakeNode("T:A", "ProjectA");
        var b = MakeNode("T:B", "ProjectA");
        var intra = MakeIntraEdge("T:A", "T:B");
        var sut = await BuildService([a, b], [intra]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A"), crossProjectOnly: true))
            results.Add(e);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReferencesAsync_BackwardCompat_DefaultFalse_ReturnsAllEdges()
    {
        var a = MakeNode("T:A", "ProjectA");
        var b = MakeNode("T:B", "ProjectA");
        var c = MakeNode("T:C", "ProjectB");
        var intra = MakeIntraEdge("T:A", "T:B");
        var cross = MakeCrossEdge("T:A", "T:C");
        var sut = await BuildService([a, b, c], [intra, cross]);

        // Call without crossProjectOnly — default is false, all edges returned
        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().HaveCount(2);
        results.Should().Contain(intra);
        results.Should().Contain(cross);
    }
}
