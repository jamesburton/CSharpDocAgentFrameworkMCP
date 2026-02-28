using DocAgent.Core;
using DocAgent.Indexing;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

public class BM25SearchIndexTests
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

    private static SymbolNode Node(string id, string displayName, string? docSummary = null) =>
        new(
            Id:                 new SymbolId(id),
            Kind:               SymbolKind.Method,
            DisplayName:        displayName,
            FullyQualifiedName: displayName,
            PreviousIds:        [],
            Accessibility:      Accessibility.Public,
            Docs:               docSummary is null
                                    ? null
                                    : new DocComment(
                                        Summary:    docSummary,
                                        Remarks:    null,
                                        Params:     new Dictionary<string, string>(),
                                        TypeParams: new Dictionary<string, string>(),
                                        Returns:    null,
                                        Examples:   [],
                                        Exceptions: [],
                                        SeeAlso:    []),
            Span:               null,
            ReturnType:         null,
            Parameters:         Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

    private static BM25SearchIndex CreateIndex() => new(new RAMDirectory());

    // ---------- tests ---------------------------------------------------------

    [Fact]
    public async Task Search_finds_by_display_name()
    {
        using var idx = CreateIndex();
        var snap = MakeSnapshot(Node("M:Foo", "Foo"));

        await idx.IndexAsync(snap, CancellationToken.None);
        var hits = await idx.SearchToListAsync("Foo", CancellationToken.None);

        hits.Should().HaveCount(1);
        hits[0].Id.Value.Should().Be("M:Foo");
    }

    [Fact]
    public async Task Search_ranks_name_match_above_doc_match()
    {
        using var idx = CreateIndex();
        var snap = MakeSnapshot(
            Node("M:GetSymbol",  "GetSymbol",  docSummary: null),
            Node("M:Unrelated", "Unrelated",  docSummary: "GetSymbol is used here"));

        await idx.IndexAsync(snap, CancellationToken.None);
        var hits = await idx.SearchToListAsync("GetSymbol", CancellationToken.None);

        hits.Should().HaveCountGreaterOrEqualTo(2);
        hits[0].Id.Value.Should().Be("M:GetSymbol",
            because: "symbol whose name matches should rank higher than doc-text-only match");
    }

    [Fact]
    public async Task CamelCase_query_resolves_partial()
    {
        using var idx = CreateIndex();
        var snap = MakeSnapshot(Node("M:GetReferences", "GetReferences"));

        await idx.IndexAsync(snap, CancellationToken.None);
        var hits = await idx.SearchToListAsync("getRef", CancellationToken.None);

        hits.Should().NotBeEmpty(because: "'getRef' should match 'GetReferences' via CamelCase token split");
        hits[0].Id.Value.Should().Be("M:GetReferences");
    }

    [Fact]
    public async Task CamelCase_splits_acronyms()
    {
        using var idx = CreateIndex();
        var snap = MakeSnapshot(Node("T:XMLParser", "XMLParser"));

        await idx.IndexAsync(snap, CancellationToken.None);
        var hits = await idx.SearchToListAsync("XML", CancellationToken.None);

        hits.Should().NotBeEmpty(because: "'XML' should match 'XMLParser' via acronym splitting");
    }

    [Fact]
    public async Task GetAsync_returns_indexed_node()
    {
        using var idx = CreateIndex();
        var node = Node("T:Foo", "Foo");
        var snap = MakeSnapshot(node);

        await idx.IndexAsync(snap, CancellationToken.None);
        var result = await idx.GetAsync(new SymbolId("T:Foo"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Foo");
    }

    [Fact]
    public async Task GetAsync_returns_null_for_unknown()
    {
        using var idx = CreateIndex();
        var snap = MakeSnapshot(Node("T:Foo", "Foo"));

        await idx.IndexAsync(snap, CancellationToken.None);
        var result = await idx.GetAsync(new SymbolId("T:Bar"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Search_empty_index_yields_no_results()
    {
        using var idx = CreateIndex();

        var hits = await idx.SearchToListAsync("anything", CancellationToken.None);

        hits.Should().BeEmpty();
    }
}
