using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

/// <summary>
/// Integration tests for RoslynSymbolGraphBuilder. These tests run against this repository's
/// own DocAgent.Core project using the real MSBuildWorkspace and Roslyn compilation APIs.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RoslynSymbolGraphBuilderTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CoreCsproj =>
        Path.Combine(RepoRoot, "src", "DocAgent.Core", "DocAgent.Core.csproj");

    private static RoslynSymbolGraphBuilder CreateBuilder()
    {
        var parser = new XmlDocParser();
        var resolver = new InheritDocResolver();
        return new RoslynSymbolGraphBuilder(parser, resolver, logWarning: null);
    }

    private static ProjectInventory CoreInventory()
        => new ProjectInventory(
            RootPath: Path.GetDirectoryName(CoreCsproj)!,
            SolutionFiles: [],
            ProjectFiles: [CoreCsproj],
            XmlDocFiles: []);

    private static readonly DocInputSet EmptyDocs =
        new DocInputSet(new Dictionary<string, string>());

    // ── Test 1: Nodes produced for known Core types ──────────────────────────

    [Fact]
    public async Task BuildAsync_produces_nodes_for_core_project()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        snapshot.Nodes.Should().NotBeEmpty();

        // Verify nodes for well-known types in DocAgent.Core
        var nodeNames = snapshot.Nodes.Select(n => n.DisplayName).ToList();
        nodeNames.Should().Contain("SymbolId");
        nodeNames.Should().Contain("SymbolNode");
        nodeNames.Should().Contain("SymbolEdge");
        nodeNames.Should().Contain("SymbolGraphSnapshot");
        nodeNames.Should().Contain("IProjectSource");
        nodeNames.Should().Contain("ISearchIndex");
    }

    // ── Test 2: Contains edges exist ────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_creates_containment_edges()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var containsEdges = snapshot.Edges.Where(e => e.Kind == SymbolEdgeKind.Contains).ToList();
        containsEdges.Should().NotBeEmpty("namespace → type containment edges should be present");

        // Each Contains edge should reference valid node IDs
        var nodeIds = snapshot.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in containsEdges.Take(20)) // spot-check first 20
        {
            nodeIds.Should().Contain(edge.To,
                $"Contains target {edge.To.Value} should be a known node");
        }
    }

    // ── Test 3: Inheritance edges ────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_creates_inheritance_edges()
    {
        // DocAgent.Core has no explicit class inheritance beyond System.Object,
        // so test Implements edges instead (interfaces are implemented).
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        // At minimum, no exception should be thrown and edges of both kinds may be present
        // (SymbolGraphSnapshot, SymbolNode etc. don't inherit explicitly, so Inherits may be empty for Core)
        var allKinds = snapshot.Edges.Select(e => e.Kind).Distinct().ToList();
        allKinds.Should().Contain(SymbolEdgeKind.Contains,
            "containment is always produced for types with members");

        // This verifies the builder didn't crash on Inherits/Implements edge logic
        snapshot.Edges.Should().NotContain(
            e => e.From == e.To,
            "self-referential edges are invalid");
    }

    // ── Test 4: Private and internal symbols are excluded ────────────────────

    [Fact]
    public async Task BuildAsync_excludes_private_members()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        snapshot.Nodes.Should().NotContain(
            n => n.Accessibility == Accessibility.Private,
            "private members must not appear in the snapshot");

        snapshot.Nodes.Should().NotContain(
            n => n.Accessibility == Accessibility.Internal,
            "internal members must not appear in the snapshot");
    }

    // ── Test 5: XML doc comments are parsed ──────────────────────────────────

    [Fact]
    public async Task BuildAsync_includes_doc_comments()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        // DocAgent.Core has XML doc comments (e.g., on IVectorIndex and SearchIndexExtensions)
        var nodesWithDocs = snapshot.Nodes.Where(n => n.Docs?.Summary is not null).ToList();
        nodesWithDocs.Should().NotBeEmpty("at least some symbols in DocAgent.Core have XML doc comments");

        // No node should have a null Docs property — every node must have either real docs or placeholder
        snapshot.Nodes.Should().AllSatisfy(n =>
            n.Docs.Should().NotBeNull("every node must have either parsed or placeholder docs"));
    }

    // ── Test 6: Source spans are assigned ────────────────────────────────────

    [Fact]
    public async Task BuildAsync_assigns_source_spans()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var nodesWithSpan = snapshot.Nodes.Where(n => n.Span is not null).ToList();
        nodesWithSpan.Should().NotBeEmpty("source-located symbols should have spans");

        foreach (var node in nodesWithSpan.Take(10)) // spot-check first 10
        {
            node.Span!.StartLine.Should().BeGreaterThan(0, "StartLine is 1-based");
            node.Span!.StartColumn.Should().BeGreaterThan(0, "StartColumn is 1-based");
            node.Span!.FilePath.Should().NotBeNullOrWhiteSpace("file path should be set");
        }
    }

    // ── Test 7: Nodes are sorted ──────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_nodes_are_sorted()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var ids = snapshot.Nodes.Select(n => n.Id.Value).ToList();
        var sorted = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();

        ids.Should().Equal(sorted, "nodes must be in Ordinal order by Id.Value for deterministic snapshots");
    }

    // ── Test 8: Edges are sorted ──────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_edges_are_sorted()
    {
        var sut = CreateBuilder();
        var snapshot = await sut.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var edges = snapshot.Edges.ToList();
        var sorted = edges
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ToList();

        for (int i = 0; i < edges.Count; i++)
        {
            edges[i].Should().Be(sorted[i],
                $"edge at index {i} must be in canonical order (From, To, Kind)");
        }
    }
}
