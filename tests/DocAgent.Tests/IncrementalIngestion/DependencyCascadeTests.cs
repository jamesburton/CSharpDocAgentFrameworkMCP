using DocAgent.Core;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests.IncrementalIngestion;

public sealed class DependencyCascadeTests
{
    [Fact]
    public void TopologicalSort_LeavesFirst()
    {
        // A depends on B, B depends on C → order: C, B, A
        var projects = new[]
        {
            new ProjectEntry("A", "a.csproj", ["B"]),
            new ProjectEntry("B", "b.csproj", ["C"]),
            new ProjectEntry("C", "c.csproj", []),
        };
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
            new ProjectEdge("B", "C"),
        };

        var result = DependencyCascade.TopologicalSort(projects, edges).ToList();

        result.Should().HaveCount(3);
        result.IndexOf("C").Should().BeLessThan(result.IndexOf("B"));
        result.IndexOf("B").Should().BeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void TopologicalSort_ParallelBranches()
    {
        // Diamond: A→B, A→C, B→D, C→D
        var projects = new[]
        {
            new ProjectEntry("A", "a.csproj", ["B", "C"]),
            new ProjectEntry("B", "b.csproj", ["D"]),
            new ProjectEntry("C", "c.csproj", ["D"]),
            new ProjectEntry("D", "d.csproj", []),
        };
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
            new ProjectEdge("A", "C"),
            new ProjectEdge("B", "D"),
            new ProjectEdge("C", "D"),
        };

        var result = DependencyCascade.TopologicalSort(projects, edges).ToList();

        result.IndexOf("D").Should().BeLessThan(result.IndexOf("B"));
        result.IndexOf("D").Should().BeLessThan(result.IndexOf("C"));
        result.IndexOf("B").Should().BeLessThan(result.IndexOf("A"));
        result.IndexOf("C").Should().BeLessThan(result.IndexOf("A"));
    }

    [Fact]
    public void ComputeDirtySet_TransitiveClosure()
    {
        // A→B→C, C changes → dirty = {C, B, A}
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
            new ProjectEdge("B", "C"),
        };

        var dirty = DependencyCascade.ComputeDirtySet(["C"], edges);

        dirty.Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Fact]
    public void ComputeDirtySet_UnrelatedProjectClean()
    {
        // A→B, C independent. B changes → dirty = {B, A}, not C
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
        };

        var dirty = DependencyCascade.ComputeDirtySet(["B"], edges);

        dirty.Should().BeEquivalentTo(["A", "B"]);
        dirty.Should().NotContain("C");
    }

    [Fact]
    public void HasStructuralChange_SameProjects_False()
    {
        var previous = new[]
        {
            new ProjectEntry("A", "a.csproj", []),
            new ProjectEntry("B", "b.csproj", []),
        };

        var result = DependencyCascade.HasStructuralChange(previous, ["a.csproj", "b.csproj"]);
        result.Should().BeFalse();
    }

    [Fact]
    public void HasStructuralChange_AddedProject_True()
    {
        var previous = new[]
        {
            new ProjectEntry("A", "a.csproj", []),
        };

        var result = DependencyCascade.HasStructuralChange(previous, ["a.csproj", "b.csproj"]);
        result.Should().BeTrue();
    }

    [Fact]
    public void HasStructuralChange_NullPrevious_True()
    {
        var result = DependencyCascade.HasStructuralChange(null, ["a.csproj"]);
        result.Should().BeTrue();
    }

    [Fact]
    public void DetectCycles_NoCycle_ReturnsEmpty()
    {
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
            new ProjectEdge("B", "C"),
        };

        var cycles = DependencyCascade.DetectCycles(edges);
        cycles.Should().BeEmpty();
    }

    [Fact]
    public void DetectCycles_WithCycle_ReturnsCycleNodes()
    {
        var edges = new[]
        {
            new ProjectEdge("A", "B"),
            new ProjectEdge("B", "A"),
        };

        var cycles = DependencyCascade.DetectCycles(edges);
        cycles.Should().NotBeEmpty();
        // The cycle should contain both A and B
        var allCycleNodes = cycles.SelectMany(c => c).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allCycleNodes.Should().Contain("A");
        allCycleNodes.Should().Contain("B");
    }
}
