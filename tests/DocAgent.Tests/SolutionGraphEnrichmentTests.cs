using DocAgent.Core;
using DocAgent.Indexing;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

/// <summary>
/// Tests that NodeKind.Stub nodes are excluded from search indexes.
/// GRAPH-05: stub nodes (external NuGet/framework types) must not pollute BM25 search rankings.
/// </summary>
public class SolutionGraphEnrichmentTests
{
    // ---------- helpers -------------------------------------------------------

    private static SymbolGraphSnapshot MakeSnapshot(params SymbolNode[] nodes) =>
        new(
            SchemaVersion:      "v1",
            ProjectName:        "test",
            SourceFingerprint:  "fixture",
            ContentHash:        null,
            CreatedAt:          DateTimeOffset.UtcNow,
            Nodes:              nodes,
            Edges:              Array.Empty<SymbolEdge>());

    private static SymbolNode RealNode(string id, string displayName) =>
        new(
            Id:                 new SymbolId(id),
            Kind:               SymbolKind.Type,
            DisplayName:        displayName,
            FullyQualifiedName: displayName,
            PreviousIds:        [],
            Accessibility:      Accessibility.Public,
            Docs:               null,
            Span:               null,
            ReturnType:         null,
            Parameters:         Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            NodeKind:           NodeKind.Real);

    private static SymbolNode StubNode(string id, string displayName) =>
        new(
            Id:                 new SymbolId(id),
            Kind:               SymbolKind.Type,
            DisplayName:        displayName,
            FullyQualifiedName: displayName,
            PreviousIds:        [],
            Accessibility:      Accessibility.Public,
            Docs:               null,
            Span:               null,
            ReturnType:         null,
            Parameters:         Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            NodeKind:           NodeKind.Stub);

    // ---------- BM25 stub filtering -------------------------------------------

    [Fact]
    public async Task BM25SearchIndex_ExcludesStubNodesFromSearch()
    {
        var real1   = RealNode("real1", "MyRealService");
        var real2   = RealNode("real2", "AnotherRealClass");
        var stub    = StubNode("stub1", "ExternalStubType");
        var snapshot = MakeSnapshot(real1, real2, stub);

        using var index = new BM25SearchIndex(new RAMDirectory());
        await index.IndexAsync(snapshot, CancellationToken.None);

        // Stub node should not appear in search results.
        var stubResults = await index.SearchAsync("ExternalStubType").ToListAsync();
        stubResults.Should().BeEmpty("stub nodes must not appear in search results");

        // Real nodes should still be found.
        var realResults = await index.SearchAsync("MyRealService").ToListAsync();
        realResults.Should().NotBeEmpty("real nodes must remain searchable");
        realResults.Should().Contain(h => h.Id == real1.Id);
    }

    [Fact]
    public async Task BM25SearchIndex_GetAsync_DoesNotReturnStubNodes()
    {
        var real = RealNode("real1", "MyRealService");
        var stub = StubNode("stub1", "ExternalStubType");
        var snapshot = MakeSnapshot(real, stub);

        using var index = new BM25SearchIndex(new RAMDirectory());
        await index.IndexAsync(snapshot, CancellationToken.None);

        var stubNode = await index.GetAsync(stub.Id, CancellationToken.None);
        stubNode.Should().BeNull("stub nodes must not be returned by GetAsync");

        var realNode = await index.GetAsync(real.Id, CancellationToken.None);
        realNode.Should().NotBeNull("real nodes must still be retrievable via GetAsync");
        realNode!.Id.Should().Be(real.Id);
    }

    // ---------- InMemory stub filtering ---------------------------------------

    [Fact]
    public async Task InMemorySearchIndex_ExcludesStubNodesFromSearch()
    {
        var real1   = RealNode("real1", "MyRealService");
        var real2   = RealNode("real2", "AnotherRealClass");
        var stub    = StubNode("stub1", "ExternalStubType");
        var snapshot = MakeSnapshot(real1, real2, stub);

        var index = new InMemorySearchIndex();
        await index.IndexAsync(snapshot, CancellationToken.None);

        // Stub node should not appear in search results.
        var stubResults = await index.SearchAsync("ExternalStubType").ToListAsync();
        stubResults.Should().BeEmpty("stub nodes must not appear in search results");

        // Real nodes should still be found.
        var realResults = await index.SearchAsync("MyRealService").ToListAsync();
        realResults.Should().NotBeEmpty("real nodes must remain searchable");
        realResults.Should().Contain(h => h.Id == real1.Id);
    }

    [Fact]
    public async Task InMemorySearchIndex_GetAsync_DoesNotReturnStubNodes()
    {
        var real = RealNode("real1", "MyRealService");
        var stub = StubNode("stub1", "ExternalStubType");
        var snapshot = MakeSnapshot(real, stub);

        var index = new InMemorySearchIndex();
        await index.IndexAsync(snapshot, CancellationToken.None);

        var stubNode = await index.GetAsync(stub.Id, CancellationToken.None);
        stubNode.Should().BeNull("stub nodes must not be returned by GetAsync");

        var realNode = await index.GetAsync(real.Id, CancellationToken.None);
        realNode.Should().NotBeNull("real nodes must still be retrievable via GetAsync");
        realNode!.Id.Should().Be(real.Id);
    }
}

// Extension helper: collect IAsyncEnumerable<T> to List<T>
file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
