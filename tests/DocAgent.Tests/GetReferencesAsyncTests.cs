using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for KnowledgeQueryService.GetReferencesAsync covering MCPS-03.
/// Uses in-memory setup: BM25SearchIndex with RAMDirectory + temp-directory SnapshotStore.
/// </summary>
[Trait("Category", "Unit")]
public class GetReferencesAsyncTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ---------- helpers -------------------------------------------------------

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "GetReferencesAsyncTests_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static SymbolNode MakeNode(string id) =>
        new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Method,
            DisplayName: id,
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null);

    private static SymbolGraphSnapshot MakeSnapshot(SymbolNode[] nodes, SymbolEdge[]? edges = null) =>
        new(
            SchemaVersion: "v1",
            ProjectName: "TestProject",
            SourceFingerprint: "fp1",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: edges ?? Array.Empty<SymbolEdge>());

    private async Task<KnowledgeQueryService> CreateServiceAsync(SymbolNode[] nodes, SymbolEdge[]? edges = null)
    {
        var snapshot = MakeSnapshot(nodes, edges);
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var saved = await store.SaveAsync(snapshot);

        var ramDir = new RAMDirectory();
        var index = new BM25SearchIndex(ramDir);
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
    public async Task Returns_Outgoing_Edges()
    {
        // A→B (Contains); query A; expect edge returned
        var a = MakeNode("T:A");
        var b = MakeNode("T:B");
        var edge = new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Contains);
        var sut = await CreateServiceAsync([a, b], [edge]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().ContainSingle().Which.Should().Be(edge);
    }

    [Fact]
    public async Task Returns_Incoming_Edges()
    {
        // B→A (Inherits); query A; expect edge returned
        var a = MakeNode("T:A");
        var b = MakeNode("T:B");
        var edge = new SymbolEdge(new SymbolId("T:B"), new SymbolId("T:A"), SymbolEdgeKind.Inherits);
        var sut = await CreateServiceAsync([a, b], [edge]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().ContainSingle().Which.Should().Be(edge);
    }

    [Fact]
    public async Task Returns_Bidirectional()
    {
        // A has both A→B and C→A; query A; expect both returned
        var a = MakeNode("T:A");
        var b = MakeNode("T:B");
        var c = MakeNode("T:C");
        var outgoing = new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Contains);
        var incoming = new SymbolEdge(new SymbolId("T:C"), new SymbolId("T:A"), SymbolEdgeKind.Calls);
        var sut = await CreateServiceAsync([a, b, c], [outgoing, incoming]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().HaveCount(2);
        results.Should().Contain(outgoing);
        results.Should().Contain(incoming);
    }

    [Fact]
    public async Task Returns_All_EdgeTypes()
    {
        // Symbol has edges of every kind; all should be returned
        var a = MakeNode("T:A");
        var b = MakeNode("T:B");
        var edges = new[]
        {
            new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Contains),
            new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Inherits),
            new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Implements),
            new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.Calls),
            new SymbolEdge(new SymbolId("T:A"), new SymbolId("T:B"), SymbolEdgeKind.References),
        };
        var sut = await CreateServiceAsync([a, b], edges);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().HaveCount(5);
        results.Select(e => e.Kind).Should().BeEquivalentTo(
            [SymbolEdgeKind.Contains, SymbolEdgeKind.Inherits, SymbolEdgeKind.Implements,
             SymbolEdgeKind.Calls, SymbolEdgeKind.References]);
    }

    [Fact]
    public async Task Throws_SymbolNotFoundException_For_Unknown_Id()
    {
        var a = MakeNode("T:A");
        var sut = await CreateServiceAsync([a]);

        var act = async () =>
        {
            await foreach (var _ in sut.GetReferencesAsync(new SymbolId("T:Unknown"))) { }
        };

        await act.Should().ThrowAsync<SymbolNotFoundException>();
    }

    [Fact]
    public async Task Returns_Empty_When_Symbol_Exists_But_No_Edges()
    {
        // Symbol is in Nodes but no edges reference it
        var a = MakeNode("T:A");
        var b = MakeNode("T:B");
        var unrelatedEdge = new SymbolEdge(new SymbolId("T:B"), new SymbolId("T:B"), SymbolEdgeKind.Contains);
        var sut = await CreateServiceAsync([a, b], [unrelatedEdge]);

        var results = new List<SymbolEdge>();
        await foreach (var e in sut.GetReferencesAsync(new SymbolId("T:A")))
            results.Add(e);

        results.Should().BeEmpty();
    }
}
