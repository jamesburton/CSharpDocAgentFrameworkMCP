using DocAgent.Core;
using FluentAssertions;

namespace DocAgent.Tests.SemanticDiff;

using static DiffTestHelpers;

public class DependencyChangeTests
{
    [Fact]
    public void Diff_detects_new_dependency_edge()
    {
        var method = BuildMethod("TestProject.Caller");
        var callee = BuildMethod("TestProject.Callee");

        var before = BuildSnapshot([method, callee], []);
        var newEdge = new SymbolEdge(new SymbolId("TestProject.Caller"), new SymbolId("TestProject.Callee"), SymbolEdgeKind.References);
        var after  = BuildSnapshot([method, callee], [newEdge]);

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Dependency).Subject;
        change.DependencyDetail.Should().NotBeNull();
        change.DependencyDetail!.AddedEdges.Should().ContainSingle(e =>
            e.From.Value == "TestProject.Caller" && e.To.Value == "TestProject.Callee");
    }

    [Fact]
    public void Diff_detects_removed_dependency_edge()
    {
        var method = BuildMethod("TestProject.Caller");
        var callee = BuildMethod("TestProject.Callee");
        var edge   = new SymbolEdge(new SymbolId("TestProject.Caller"), new SymbolId("TestProject.Callee"), SymbolEdgeKind.References);

        var before = BuildSnapshot([method, callee], [edge]);
        var after  = BuildSnapshot([method, callee], []);

        var diff = SymbolGraphDiffer.Diff(before, after);

        var change = diff.Changes.Should().ContainSingle(c => c.Category == ChangeCategory.Dependency).Subject;
        change.DependencyDetail.Should().NotBeNull();
        change.DependencyDetail!.RemovedEdges.Should().ContainSingle(e =>
            e.From.Value == "TestProject.Caller" && e.To.Value == "TestProject.Callee");
    }
}
