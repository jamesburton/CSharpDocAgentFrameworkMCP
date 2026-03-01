using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

/// <summary>
/// Tests for project-aware search: projectFilter application and ProjectName population.
/// Phase 15 — TOOLS-01, TOOLS-03, TOOLS-06.
/// </summary>
[Trait("Category", "Unit")]
public class ProjectAwareIndexingTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ---------- helpers -------------------------------------------------------

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ProjectAwareIndexingTests_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static SymbolNode MakeNode(string id, string displayName, string? project) =>
        new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: displayName,
            FullyQualifiedName: displayName,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            ProjectOrigin: project,
            NodeKind: NodeKind.Real);

    private static SymbolGraphSnapshot BuildSnapshot(params SymbolNode[] nodes) =>
        new(
            SchemaVersion: "v1",
            ProjectName: "TestProject",
            SourceFingerprint: "fp1",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: Array.Empty<SymbolEdge>());

    private async Task<KnowledgeQueryService> BuildService(SymbolGraphSnapshot snapshot)
    {
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
    public async Task SearchAsync_NoProjectFilter_ReturnsResultsFromAllProjects()
    {
        var nodeA = MakeNode("T:ServiceA", "ServiceA", "ProjectA");
        var nodeB = MakeNode("T:ServiceB", "ServiceB", "ProjectB");
        var sut = await BuildService(BuildSnapshot(nodeA, nodeB));

        var result = await sut.SearchAsync("Service");

        result.Success.Should().BeTrue();
        var items = result.Value!.Payload;
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.ProjectName == "ProjectA");
        items.Should().Contain(i => i.ProjectName == "ProjectB");
        items.Should().AllSatisfy(i => i.ProjectName.Should().NotBeNull());
    }

    [Fact]
    public async Task SearchAsync_WithProjectFilter_ReturnsOnlyMatchingProject()
    {
        var nodeA = MakeNode("T:ServiceA", "ServiceA", "ProjectA");
        var nodeB = MakeNode("T:ServiceB", "ServiceB", "ProjectB");
        var sut = await BuildService(BuildSnapshot(nodeA, nodeB));

        var result = await sut.SearchAsync("Service", projectFilter: "ProjectA");

        result.Success.Should().BeTrue();
        var items = result.Value!.Payload;
        items.Should().NotBeEmpty();
        items.Should().AllSatisfy(i => i.ProjectName.Should().Be("ProjectA"));
    }

    [Fact]
    public async Task SearchAsync_WithUnknownProjectFilter_ReturnsEmpty()
    {
        var nodeA = MakeNode("T:ServiceA", "ServiceA", "ProjectA");
        var sut = await BuildService(BuildSnapshot(nodeA));

        var result = await sut.SearchAsync("Service", projectFilter: "NonExistent");

        result.Success.Should().BeTrue();
        result.Value!.Payload.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ProjectNamePopulated_FromNodeProjectOrigin()
    {
        var nodeA = MakeNode("T:Alpha", "Alpha", "ProjectA");
        var nodeB = MakeNode("T:Beta", "Beta", "ProjectB");
        var sut = await BuildService(BuildSnapshot(nodeA, nodeB));

        var result = await sut.SearchAsync("Alpha");

        result.Success.Should().BeTrue();
        var item = result.Value!.Payload.Should().ContainSingle().Subject;
        item.ProjectName.Should().Be("ProjectA");
    }

    [Fact]
    public async Task SearchAsync_BackwardCompat_NullProjectOrigin_ReturnsNullProjectName()
    {
        // Legacy single-project node with no ProjectOrigin
        var legacyNode = MakeNode("T:LegacyClass", "LegacyClass", null);
        var sut = await BuildService(BuildSnapshot(legacyNode));

        var result = await sut.SearchAsync("LegacyClass");

        result.Success.Should().BeTrue();
        var item = result.Value!.Payload.Should().ContainSingle().Subject;
        item.ProjectName.Should().BeNull();
    }
}
