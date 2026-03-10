using DocAgent.Core;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Tests;

public class SolutionSnapshotTests
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private static SymbolGraphSnapshot BuildMinimalSnapshot(string projectName)
    {
        var node = new SymbolNode(
            Id: new SymbolId($"{projectName}.RootType"),
            Kind: SymbolKind.Type,
            DisplayName: "RootType",
            FullyQualifiedName: $"{projectName}.RootType",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

        return new SymbolGraphSnapshot(
            SchemaVersion: "1.0.0",
            ProjectName: projectName,
            SourceFingerprint: "fp123",
            ContentHash: null,
            CreatedAt: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            Nodes: [node],
            Edges: []);
    }

    [Fact]
    public void ProjectEntry_Construction_And_Equality()
    {
        // Use the same list instance to ensure record equality works (IReadOnlyList uses reference equality)
        var deps = new List<string> { "ProjectB" };
        var entry1 = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", deps);
        var entry2 = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", deps);
        var entry3 = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", ["ProjectC"]);

        entry1.Should().Be(entry2);
        entry1.Name.Should().Be("ProjectA");
        entry1.Path.Should().Be("src/ProjectA/ProjectA.csproj");
        entry1.DependsOn.Should().ContainSingle().Which.Should().Be("ProjectB");
        entry1.Should().NotBe(entry3);
    }

    [Fact]
    public void ProjectEdge_Construction_And_Equality()
    {
        var edge1 = new ProjectEdge("ProjectA", "ProjectB");
        var edge2 = new ProjectEdge("ProjectA", "ProjectB");
        var edge3 = new ProjectEdge("ProjectA", "ProjectC");

        edge1.Should().Be(edge2);
        edge1.Should().NotBe(edge3);
    }

    [Fact]
    public void SolutionSnapshot_Construction_With_Empty_Projects()
    {
        var createdAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshot = new SolutionSnapshot(
            SolutionName: null,
            Projects: [],
            ProjectDependencies: [],
            ProjectSnapshots: [],
            CreatedAt: createdAt);

        snapshot.SolutionName.Should().BeNull();
        snapshot.Projects.Should().BeEmpty();
        snapshot.ProjectDependencies.Should().BeEmpty();
        snapshot.ProjectSnapshots.Should().BeEmpty();
        snapshot.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void SolutionSnapshot_With_Multiple_Projects()
    {
        var projectA = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", ["ProjectB"]);
        var projectB = new ProjectEntry("ProjectB", "src/ProjectB/ProjectB.csproj", []);
        var edge = new ProjectEdge("ProjectA", "ProjectB");
        var snapshotA = BuildMinimalSnapshot("ProjectA");
        var snapshotB = BuildMinimalSnapshot("ProjectB");

        var summaryA = new ProjectSnapshotSummary(
            ProjectName: snapshotA.ProjectName,
            FilePath: null,
            NodeCount: snapshotA.Nodes.Count,
            EdgeCount: snapshotA.Edges.Count,
            ContentHash: snapshotA.ContentHash);
        var summaryB = new ProjectSnapshotSummary(
            ProjectName: snapshotB.ProjectName,
            FilePath: null,
            NodeCount: snapshotB.Nodes.Count,
            EdgeCount: snapshotB.Edges.Count,
            ContentHash: snapshotB.ContentHash);

        var solution = new SolutionSnapshot(
            SolutionName: "MySolution",
            Projects: [projectA, projectB],
            ProjectDependencies: [edge],
            ProjectSnapshots: [summaryA, summaryB],
            CreatedAt: DateTimeOffset.UtcNow);

        solution.Projects.Count.Should().Be(2);
        solution.ProjectDependencies.Count.Should().Be(1);
        solution.ProjectSnapshots.Count.Should().Be(2);
        solution.SolutionName.Should().Be("MySolution");
    }

    [Fact]
    public void SolutionSnapshot_MessagePack_Roundtrip()
    {
        var projectA = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", ["ProjectB"]);
        var projectB = new ProjectEntry("ProjectB", "src/ProjectB/ProjectB.csproj", []);
        var edge = new ProjectEdge("ProjectA", "ProjectB");
        var snapshotA = BuildMinimalSnapshot("ProjectA");
        var snapshotB = BuildMinimalSnapshot("ProjectB");

        var summaryA2 = new ProjectSnapshotSummary(
            ProjectName: snapshotA.ProjectName,
            FilePath: null,
            NodeCount: snapshotA.Nodes.Count,
            EdgeCount: snapshotA.Edges.Count,
            ContentHash: snapshotA.ContentHash);
        var summaryB2 = new ProjectSnapshotSummary(
            ProjectName: snapshotB.ProjectName,
            FilePath: null,
            NodeCount: snapshotB.Nodes.Count,
            EdgeCount: snapshotB.Edges.Count,
            ContentHash: snapshotB.ContentHash);

        var solution = new SolutionSnapshot(
            SolutionName: "MySolution",
            Projects: [projectA, projectB],
            ProjectDependencies: [edge],
            ProjectSnapshots: [summaryA2, summaryB2],
            CreatedAt: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

        byte[] bytes = MessagePackSerializer.Serialize(solution, Options);
        var deserialized = MessagePackSerializer.Deserialize<SolutionSnapshot>(bytes, Options);

        deserialized.Should().BeEquivalentTo(solution);
    }

    [Fact]
    public void SolutionSnapshot_DAG_Structure()
    {
        var projectA = new ProjectEntry("ProjectA", "src/ProjectA/ProjectA.csproj", ["ProjectB"]);
        var projectB = new ProjectEntry("ProjectB", "src/ProjectB/ProjectB.csproj", ["ProjectC"]);
        var projectC = new ProjectEntry("ProjectC", "src/ProjectC/ProjectC.csproj", []);
        var edgeAB = new ProjectEdge("ProjectA", "ProjectB");
        var edgeBC = new ProjectEdge("ProjectB", "ProjectC");

        var solution = new SolutionSnapshot(
            SolutionName: "ChainedSolution",
            Projects: [projectA, projectB, projectC],
            ProjectDependencies: [edgeAB, edgeBC],
            ProjectSnapshots: [],
            CreatedAt: DateTimeOffset.UtcNow);

        solution.Projects.Count.Should().Be(3);
        solution.ProjectDependencies.Count.Should().Be(2);
        solution.ProjectDependencies[0].From.Should().Be("ProjectA");
        solution.ProjectDependencies[0].To.Should().Be("ProjectB");
        solution.ProjectDependencies[1].From.Should().Be("ProjectB");
        solution.ProjectDependencies[1].To.Should().Be("ProjectC");
        solution.Projects[0].DependsOn.Should().ContainSingle().Which.Should().Be("ProjectB");
        solution.Projects[1].DependsOn.Should().ContainSingle().Which.Should().Be("ProjectC");
        solution.Projects[2].DependsOn.Should().BeEmpty();
    }
}
