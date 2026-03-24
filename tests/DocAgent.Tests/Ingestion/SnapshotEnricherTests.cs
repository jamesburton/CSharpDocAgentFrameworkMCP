using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using static DocAgent.Tests.Ingestion.ScriptTestHelpers;

namespace DocAgent.Tests.Ingestion;

public class SnapshotEnricherTests
{
    [Fact]
    public void Enrichment_AddsCrossLanguageEdges_ForMixedSnapshot()
    {
        var scriptNode = BuildScriptNode("script:build.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");

        var edges = new[]
        {
            BuildEdge("script:build.ps1", "System.IO.Path", SymbolEdgeKind.References)
        };

        var snapshot = BuildMixedSnapshot([scriptNode, typeNode], edges);

        var enriched = SnapshotEnricher.EnrichWithCrossLanguageEdges(snapshot);

        enriched.Edges.Should().HaveCountGreaterThan(snapshot.Edges.Count);
        enriched.Edges.Should().Contain(e =>
            e.From.Value == "script:build.ps1" &&
            e.To.Value == "T:System.IO.Path" &&
            e.Kind == SymbolEdgeKind.References &&
            e.Scope == EdgeScope.CrossProject);
    }

    [Fact]
    public void Enrichment_PreservesExistingNodesAndEdges()
    {
        var scriptNode = BuildScriptNode("script:build.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");
        var nsNode = BuildCSharpNamespaceNode("CoreLib", "CoreLib");

        var originalEdge = BuildEdge("N:CoreLib", "T:System.IO.Path", SymbolEdgeKind.Contains);
        var triggerEdge = BuildEdge("script:build.ps1", "System.IO.Path", SymbolEdgeKind.References);

        var snapshot = BuildMixedSnapshot(
            [scriptNode, typeNode, nsNode],
            [originalEdge, triggerEdge]);

        var enriched = SnapshotEnricher.EnrichWithCrossLanguageEdges(snapshot);

        // All original nodes preserved
        enriched.Nodes.Should().HaveCount(3);
        enriched.Nodes.Should().Contain(scriptNode);
        enriched.Nodes.Should().Contain(typeNode);
        enriched.Nodes.Should().Contain(nsNode);

        // Original edges preserved
        enriched.Edges.Should().Contain(originalEdge);
        enriched.Edges.Should().Contain(triggerEdge);
    }

    [Fact]
    public void EmptySnapshot_ReturnsUnchanged()
    {
        var snapshot = BuildMixedSnapshot([], []);

        var enriched = SnapshotEnricher.EnrichWithCrossLanguageEdges(snapshot);

        enriched.Nodes.Should().BeEmpty();
        enriched.Edges.Should().BeEmpty();
    }

    [Fact]
    public void Idempotent_EnrichingTwice_ProducesSameResult()
    {
        var scriptNode = BuildScriptNode("script:build.ps1", SymbolKind.Script);
        var typeNode = BuildCSharpTypeNode("System.IO.Path", "CoreLib");

        var edges = new[]
        {
            BuildEdge("script:build.ps1", "System.IO.Path", SymbolEdgeKind.References)
        };

        var snapshot = BuildMixedSnapshot([scriptNode, typeNode], edges);

        var enrichedOnce = SnapshotEnricher.EnrichWithCrossLanguageEdges(snapshot);
        var enrichedTwice = SnapshotEnricher.EnrichWithCrossLanguageEdges(enrichedOnce);

        enrichedTwice.Edges.Should().HaveCount(enrichedOnce.Edges.Count);
        enrichedTwice.Edges.Should().BeEquivalentTo(enrichedOnce.Edges);
    }
}
