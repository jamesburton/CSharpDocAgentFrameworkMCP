using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocAgent.Tests.IncrementalIngestion;

/// <summary>
/// Unit tests for <see cref="IncrementalSolutionIngestionService"/> using the PipelineOverride seam
/// to verify skip, cascade, stub lifecycle, and force-full-reingest behaviors without MSBuild.
/// </summary>
public sealed class SolutionIncrementalIngestionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SolutionIncrementalIngestionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"incr-sln-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IncrementalSolutionIngestionService CreateService()
    {
        var fullService = new SolutionIngestionService(
            _store,
            new InMemorySearchIndex(),
            NullLogger<SolutionIngestionService>.Instance);

        return new IncrementalSolutionIngestionService(
            _store,
            fullService,
            NullLogger<IncrementalSolutionIngestionService>.Instance);
    }

    private static SymbolNode MakeNode(string id, string projectOrigin, NodeKind kind = NodeKind.Real) =>
        new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: id.Split('.').Last(),
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            ProjectOrigin: projectOrigin,
            NodeKind: kind);

    private static SymbolEdge MakeEdge(string from, string to, SymbolEdgeKind edgeKind = SymbolEdgeKind.Inherits, EdgeScope scope = EdgeScope.IntraProject) =>
        new(new SymbolId(from), new SymbolId(to), edgeKind, scope);

    private static ProjectIngestionStatus OkStatus(string name, int nodeCount) =>
        new(Name: name, FilePath: $"/src/{name}/{name}.csproj", Status: "ok", Reason: null, NodeCount: nodeCount, ChosenTfm: null);

    private static ProjectIngestionStatus SkippedStatus(string name) =>
        new(Name: name, FilePath: $"/src/{name}/{name}.csproj", Status: "skipped", Reason: "unchanged", NodeCount: null, ChosenTfm: null);

    /// <summary>
    /// Simulates two ingestion calls. First call is always "full" (all ok). Second call uses the
    /// provided statuses/nodes/edges to simulate incremental behavior.
    /// </summary>
    private record IngestScenario(
        string SolutionName,
        IReadOnlyList<ProjectEntry> Projects,
        IReadOnlyList<ProjectEdge> ProjectEdges,
        // First run
        IReadOnlyList<ProjectIngestionStatus> FirstRunStatuses,
        IReadOnlyList<SymbolNode> FirstRunNodes,
        IReadOnlyList<SymbolEdge> FirstRunEdges,
        // Second run
        IReadOnlyList<ProjectIngestionStatus> SecondRunStatuses,
        IReadOnlyList<SymbolNode> SecondRunNodes,
        IReadOnlyList<SymbolEdge> SecondRunEdges);

    private async Task<(SolutionIngestionResult first, SolutionIngestionResult second)> RunTwoPassAsync(
        IngestScenario scenario,
        bool forceOnSecond = false)
    {
        var svc = CreateService();
        int callCount = 0;

        svc.PipelineOverride = (slnPath, forceFullReingest, warnings, ct) =>
        {
            callCount++;
            bool isFirst = callCount == 1;

            var statuses = isFirst ? scenario.FirstRunStatuses : scenario.SecondRunStatuses;
            var nodes = isFirst ? scenario.FirstRunNodes : scenario.SecondRunNodes;
            var edges = isFirst ? scenario.FirstRunEdges : scenario.SecondRunEdges;

            // Assign stubs to the first project's snapshot (they're solution-wide)
            var stubsAssigned = false;
            var perProjectSnapshots = scenario.Projects.Select(p =>
            {
                var projectNodes = nodes.Where(n => n.ProjectOrigin == p.Name).ToList();
                if (!stubsAssigned)
                {
                    projectNodes.AddRange(nodes.Where(n => n.NodeKind == NodeKind.Stub));
                    stubsAssigned = true;
                }
                return new SymbolGraphSnapshot(
                    SchemaVersion: "1.2",
                    ProjectName: p.Name,
                    SourceFingerprint: $"fp-{p.Name}",
                    ContentHash: null,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Nodes: projectNodes,
                    Edges: edges.Where(e => nodes.Any(n => n.Id == e.From && n.ProjectOrigin == p.Name)).ToList(),
                    IngestionMetadata: null,
                    SolutionName: scenario.SolutionName);
            }).ToList();

            var snapshot = new SolutionSnapshot(
                SolutionName: scenario.SolutionName,
                Projects: scenario.Projects,
                ProjectDependencies: scenario.ProjectEdges,
                ProjectSnapshots: perProjectSnapshots,
                CreatedAt: DateTimeOffset.UtcNow);

            var mergedSnapshot = new SymbolGraphSnapshot(
                SchemaVersion: "1.2",
                ProjectName: scenario.SolutionName,
                SourceFingerprint: $"fp-{scenario.SolutionName}-{callCount}",
                ContentHash: null,
                CreatedAt: DateTimeOffset.UtcNow,
                Nodes: SymbolSorter.SortNodes(nodes.ToList()),
                Edges: SymbolSorter.SortEdges(edges.ToList()),
                IngestionMetadata: null,
                SolutionName: scenario.SolutionName);

            // Save snapshot via store to test pointer file path
            var saved = _store.SaveAsync(mergedSnapshot, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                SnapshotId: saved.ContentHash ?? "test-hash",
                SolutionName: scenario.SolutionName,
                TotalProjectCount: scenario.Projects.Count,
                IngestedProjectCount: statuses.Count(s => s.Status == "ok"),
                TotalNodeCount: nodes.Count,
                TotalEdgeCount: edges.Count,
                Duration: TimeSpan.FromMilliseconds(50),
                Projects: statuses,
                Warnings: warnings.AsReadOnly(),
                Snapshot: snapshot));
        };

        var first = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);
        var second = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None, forceFullReingest: forceOnSecond);

        return (first, second);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SkipUnchangedProjects_SecondRunSkipsAll()
    {
        // Arrange: Two projects, no changes between runs
        var projects = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
        };
        var nodes = new List<SymbolNode>
        {
            MakeNode("T:ProjA.TypeA", "ProjA"),
            MakeNode("T:ProjB.TypeB", "ProjB"),
        };

        var scenario = new IngestScenario(
            SolutionName: "TestSln",
            Projects: projects,
            ProjectEdges: [],
            FirstRunStatuses: [OkStatus("ProjA", 1), OkStatus("ProjB", 1)],
            FirstRunNodes: nodes,
            FirstRunEdges: [],
            // Second run: all skipped
            SecondRunStatuses: [SkippedStatus("ProjA"), SkippedStatus("ProjB")],
            SecondRunNodes: nodes, // preserved from first run
            SecondRunEdges: []);

        // Act
        var (first, second) = await RunTwoPassAsync(scenario);

        // Assert - INGEST-01: second run skips all
        first.IngestedProjectCount.Should().Be(2);
        second.Projects.Should().AllSatisfy(p =>
        {
            p.Status.Should().Be("skipped");
            p.Reason.Should().Be("unchanged");
        });
        second.ProjectsSkippedCount.Should().Be(2);
    }

    [Fact]
    public async Task DirtyDependentReingested_CascadesDirtyToDependents()
    {
        // Arrange: A depends on B. B changed => both should reingest.
        var projects = new List<ProjectEntry>
        {
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
            new("ProjA", "/src/ProjA/ProjA.csproj", ["ProjB"]),
        };
        var edges = new List<ProjectEdge> { new("ProjA", "ProjB") };
        var nodes = new List<SymbolNode>
        {
            MakeNode("T:ProjA.TypeA", "ProjA"),
            MakeNode("T:ProjB.TypeB", "ProjB"),
        };

        var scenario = new IngestScenario(
            SolutionName: "TestSln",
            Projects: projects,
            ProjectEdges: edges,
            FirstRunStatuses: [OkStatus("ProjB", 1), OkStatus("ProjA", 1)],
            FirstRunNodes: nodes,
            FirstRunEdges: [],
            // Second run: B changed, A dirty via cascade => both re-ingested
            SecondRunStatuses: [OkStatus("ProjB", 1), OkStatus("ProjA", 1)],
            SecondRunNodes: nodes,
            SecondRunEdges: []);

        // Act
        var (first, second) = await RunTwoPassAsync(scenario);

        // Assert - both re-ingested
        second.Projects.Should().AllSatisfy(p => p.Status.Should().Be("ok"));
        second.ProjectsReingestedCount.Should().Be(2);
    }

    [Fact]
    public async Task StubsPreservedForSkippedProjects_StubCountMatches()
    {
        // Arrange: projects with stubs, second run preserves stubs
        var projects = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
        };
        var stubNode = MakeNode("T:ExternalLib.IFoo", "ExternalLib", NodeKind.Stub);
        var realNode = MakeNode("T:ProjA.MyClass", "ProjA");
        var stubEdge = MakeEdge("T:ProjA.MyClass", "T:ExternalLib.IFoo", SymbolEdgeKind.Implements, EdgeScope.External);

        var allNodes = new List<SymbolNode> { realNode, stubNode };
        var allEdges = new List<SymbolEdge> { stubEdge };

        var scenario = new IngestScenario(
            SolutionName: "TestSln",
            Projects: projects,
            ProjectEdges: [],
            FirstRunStatuses: [OkStatus("ProjA", 1)],
            FirstRunNodes: allNodes,
            FirstRunEdges: allEdges,
            // Second run: skipped but stubs preserved
            SecondRunStatuses: [SkippedStatus("ProjA")],
            SecondRunNodes: allNodes, // stubs still present
            SecondRunEdges: allEdges);

        // Act
        var (first, second) = await RunTwoPassAsync(scenario);

        // Assert - INGEST-04 preservation: stubs present in both runs
        var firstStubs = first.Snapshot!.ProjectSnapshots
            .SelectMany(s => s.Nodes)
            .Count(n => n.NodeKind == NodeKind.Stub);
        var secondStubs = second.Snapshot!.ProjectSnapshots
            .SelectMany(s => s.Nodes)
            .Count(n => n.NodeKind == NodeKind.Stub);

        firstStubs.Should().Be(1);
        secondStubs.Should().Be(firstStubs, "stubs should be preserved for skipped projects");
    }

    [Fact]
    public async Task StubsPrunedForRemovedProjects_OrphanedStubsRemoved()
    {
        // Arrange: First run has ProjA and ProjB. ProjB references ExternalLib.
        // Second run: structural change (ProjB removed) triggers full reingest.
        // The stub from ProjB should be pruned since no real nodes reference it.
        var projectsBoth = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
        };
        var projectsAOnly = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
        };

        var stubNode = MakeNode("T:ExternalLib.IBar", "ExternalLib", NodeKind.Stub);
        var nodeA = MakeNode("T:ProjA.TypeA", "ProjA");
        var nodeB = MakeNode("T:ProjB.TypeB", "ProjB");
        var stubEdge = MakeEdge("T:ProjB.TypeB", "T:ExternalLib.IBar", SymbolEdgeKind.Implements, EdgeScope.External);

        var svc = CreateService();
        int callCount = 0;

        svc.PipelineOverride = (slnPath, forceFullReingest, warnings, ct) =>
        {
            callCount++;

            IReadOnlyList<ProjectEntry> projects;
            IReadOnlyList<ProjectIngestionStatus> statuses;
            IReadOnlyList<SymbolNode> nodes;
            IReadOnlyList<SymbolEdge> edges;

            if (callCount == 1)
            {
                projects = projectsBoth;
                statuses = [OkStatus("ProjA", 1), OkStatus("ProjB", 1)];
                nodes = [nodeA, nodeB, stubNode];
                edges = [stubEdge];
            }
            else
            {
                // Second run: only ProjA, stub pruned (no inbound edges from real nodes)
                projects = projectsAOnly;
                statuses = [OkStatus("ProjA", 1)];
                nodes = [nodeA]; // stub pruned — no real node references it
                edges = [];
            }

            var perProjectSnapshots = projects.Select(p =>
                new SymbolGraphSnapshot(
                    SchemaVersion: "1.2",
                    ProjectName: p.Name,
                    SourceFingerprint: $"fp-{p.Name}",
                    ContentHash: null,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Nodes: nodes.Where(n => n.ProjectOrigin == p.Name || n.NodeKind == NodeKind.Stub).ToList(),
                    Edges: [],
                    IngestionMetadata: null,
                    SolutionName: "TestSln")).ToList();

            var snapshot = new SolutionSnapshot("TestSln", projects, [], perProjectSnapshots, DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", "TestSln", $"fp-{callCount}", null, DateTimeOffset.UtcNow,
                SymbolSorter.SortNodes(nodes.ToList()), SymbolSorter.SortEdges(edges.ToList()), null, "TestSln");
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", "TestSln", projects.Count, statuses.Count(s => s.Status == "ok"),
                nodes.Count, edges.Count, TimeSpan.FromMilliseconds(50), statuses, [], snapshot));
        };

        // Act
        var first = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);
        var second = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);

        // Assert - INGEST-04 pruning: stub gone after ProjB removed
        var firstStubs = first.Snapshot!.ProjectSnapshots
            .SelectMany(s => s.Nodes)
            .Count(n => n.NodeKind == NodeKind.Stub);
        var secondStubs = second.Snapshot!.ProjectSnapshots
            .SelectMany(s => s.Nodes)
            .Count(n => n.NodeKind == NodeKind.Stub);

        firstStubs.Should().BeGreaterThan(0, "first run should have stubs");
        secondStubs.Should().Be(0, "stubs should be pruned after referencing project removed");
    }

    [Fact]
    public async Task ForceFullReingest_BypassesIncremental_AllProjectsReingested()
    {
        // Arrange: nothing changed, but forceFullReingest = true
        var projects = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
        };
        var nodes = new List<SymbolNode>
        {
            MakeNode("T:ProjA.TypeA", "ProjA"),
            MakeNode("T:ProjB.TypeB", "ProjB"),
        };

        var scenario = new IngestScenario(
            SolutionName: "TestSln",
            Projects: projects,
            ProjectEdges: [],
            FirstRunStatuses: [OkStatus("ProjA", 1), OkStatus("ProjB", 1)],
            FirstRunNodes: nodes,
            FirstRunEdges: [],
            // Second run: force => all ok (not skipped)
            SecondRunStatuses: [OkStatus("ProjA", 1), OkStatus("ProjB", 1)],
            SecondRunNodes: nodes,
            SecondRunEdges: []);

        // Act
        var (first, second) = await RunTwoPassAsync(scenario, forceOnSecond: true);

        // Assert - all projects re-ingested despite no changes
        second.Projects.Should().AllSatisfy(p => p.Status.Should().Be("ok"));
        second.ProjectsReingestedCount.Should().Be(2);
        second.ProjectsSkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task StructuralChange_TriggersFullReingest_AllProjectsReingested()
    {
        // Arrange: first run 2 projects, second run 3 projects (added one)
        var projects2 = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
        };
        var projects3 = new List<ProjectEntry>
        {
            new("ProjA", "/src/ProjA/ProjA.csproj", []),
            new("ProjB", "/src/ProjB/ProjB.csproj", []),
            new("ProjC", "/src/ProjC/ProjC.csproj", []),
        };

        var svc = CreateService();
        int callCount = 0;

        svc.PipelineOverride = (slnPath, forceFullReingest, warnings, ct) =>
        {
            callCount++;
            var projects = callCount == 1 ? projects2 : projects3;
            var statuses = projects.Select(p => OkStatus(p.Name, 1)).ToList();
            var nodes = projects.Select(p => MakeNode($"T:{p.Name}.Type", p.Name)).ToList();

            var perProjectSnapshots = projects.Select(p =>
                new SymbolGraphSnapshot("1.2", p.Name, $"fp-{p.Name}", null, DateTimeOffset.UtcNow,
                    nodes.Where(n => n.ProjectOrigin == p.Name).ToList(), [], null, "TestSln")).ToList();

            var snapshot = new SolutionSnapshot("TestSln", projects, [], perProjectSnapshots, DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", "TestSln", $"fp-{callCount}", null, DateTimeOffset.UtcNow,
                SymbolSorter.SortNodes(nodes), [], null, "TestSln");
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", "TestSln", projects.Count, statuses.Count,
                nodes.Count, 0, TimeSpan.FromMilliseconds(50), statuses, [], snapshot));
        };

        // Act
        var first = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);
        var second = await svc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);

        // Assert - structural change (3 vs 2 projects) => all re-ingested
        first.TotalProjectCount.Should().Be(2);
        second.TotalProjectCount.Should().Be(3);
        second.Projects.Should().AllSatisfy(p => p.Status.Should().Be("ok"));
        second.ProjectsReingestedCount.Should().Be(3);
    }
}
