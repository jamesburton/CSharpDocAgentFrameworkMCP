using DocAgent.Core;
using DocAgent.Indexing;
using FluentAssertions;

namespace DocAgent.Tests;

public class InMemorySearchIndexTests
{
    [Fact]
    public async Task Search_finds_by_display_name()
    {
        var idx = new InMemorySearchIndex();
        var snap = new SymbolGraphSnapshot(
            SchemaVersion: "v1",
            SourceFingerprint: "fixture",
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: new[] { new SymbolNode(new SymbolId("T:Foo"), SymbolKind.Type, "Foo", "Foo", null, null) },
            Edges: Array.Empty<SymbolEdge>());

        await idx.IndexAsync(snap, CancellationToken.None);
        var hits = await idx.SearchAsync("Foo", CancellationToken.None);

        hits.Should().HaveCount(1);
        hits[0].Id.Value.Should().Be("T:Foo");
    }
}
