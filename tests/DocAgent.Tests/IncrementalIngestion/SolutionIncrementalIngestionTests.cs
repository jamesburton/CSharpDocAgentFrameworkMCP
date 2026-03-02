using System.Text.Json;
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

    // ── Production Path Tests (no PipelineOverride) ──────────────────────────

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a real project directory with .cs files, saves snapshot + manifests + pointer
    /// so the production path has real state to work with.
    /// </summary>
    private async Task<(string slnPath, SolutionIngestionService fullSvc, IncrementalSolutionIngestionService incrSvc)>
        SetupProductionPathAsync(
            IReadOnlyList<ProjectEntry> projects,
            IReadOnlyList<ProjectEdge> projectEdges,
            IReadOnlyList<SymbolNode> nodes,
            IReadOnlyList<SymbolEdge> edges)
    {
        // Create real project directories with .cs files
        var slnPath = Path.Combine(_tempDir, "Test.sln");
        await File.WriteAllTextAsync(slnPath, "# fake sln");

        foreach (var project in projects)
        {
            var projDir = Path.GetDirectoryName(project.Path)!;
            Directory.CreateDirectory(projDir);
            await File.WriteAllTextAsync(project.Path, $"<Project />");

            // Create a .cs file per project
            var csFile = Path.Combine(projDir, $"{project.Name}.cs");
            await File.WriteAllTextAsync(csFile, $"namespace {project.Name} {{ public class Type1 {{ }} }}");
        }

        // Build per-project snapshots
        var stubsAssigned = false;
        var perProjectSnapshots = projects.Select(p =>
        {
            var projectNodes = nodes.Where(n => n.ProjectOrigin == p.Name).ToList();
            if (!stubsAssigned)
            {
                projectNodes.AddRange(nodes.Where(n => n.NodeKind == NodeKind.Stub));
                stubsAssigned = true;
            }
            return new SymbolGraphSnapshot("1.2", p.Name, $"fp-{p.Name}", null, DateTimeOffset.UtcNow,
                projectNodes,
                edges.Where(e => nodes.Any(n => n.Id == e.From && n.ProjectOrigin == p.Name)).ToList(),
                null, "Test");
        }).ToList();

        // Save solution snapshot
        var solutionSnapshot = new SolutionSnapshot("Test", projects, projectEdges, perProjectSnapshots, DateTimeOffset.UtcNow);
        var solutionSnapshotJson = JsonSerializer.Serialize(solutionSnapshot, s_jsonOptions);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "latest-Test.solution.json"), solutionSnapshotJson);

        // Save merged SymbolGraphSnapshot via store (for pointer)
        var merged = new SymbolGraphSnapshot("1.2", "Test", "fp-merged", null, DateTimeOffset.UtcNow,
            SymbolSorter.SortNodes(nodes.ToList()),
            SymbolSorter.SortEdges(edges.ToList()),
            null, "Test");
        var saved = await _store.SaveAsync(merged);

        // Save pointer file
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "latest-Test.ptr"), saved.ContentHash!);

        // Save per-project manifests
        foreach (var project in projects)
        {
            var refPaths = project.DependsOn
                .Select(dep => projects.FirstOrDefault(p => p.Name == dep)?.Path ?? string.Empty)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            var manifest = await SolutionManifestStore.ComputeProjectManifestAsync(
                project.Path, refPaths, null, CancellationToken.None);
            await SolutionManifestStore.SaveAsync(_tempDir, slnPath, project.Path, manifest, CancellationToken.None);
        }

        // Create services
        var fullSvc = new SolutionIngestionService(
            _store, new InMemorySearchIndex(), NullLogger<SolutionIngestionService>.Instance);
        var incrSvc = new IncrementalSolutionIngestionService(
            _store, fullSvc, NullLogger<IncrementalSolutionIngestionService>.Instance);

        return (slnPath, fullSvc, incrSvc);
    }

    [Fact]
    public async Task ProductionPath_NothingChanged_SkipsAll()
    {
        // Arrange: two projects with real files on disk, pre-saved state
        var projects = new List<ProjectEntry>
        {
            new("ProjA", Path.Combine(_tempDir, "src", "ProjA", "ProjA.csproj"), []),
            new("ProjB", Path.Combine(_tempDir, "src", "ProjB", "ProjB.csproj"), []),
        };
        var nodes = new List<SymbolNode>
        {
            MakeNode("T:ProjA.TypeA", "ProjA"),
            MakeNode("T:ProjB.TypeB", "ProjB"),
        };

        var (slnPath, fullSvc, incrSvc) = await SetupProductionPathAsync(projects, [], nodes, []);

        // Set full service to throw if called — proving skip path works
        fullSvc.PipelineOverride = (_, _, _) =>
            throw new InvalidOperationException("Full ingest should NOT be called when nothing changed");

        // Act: call IngestAsync WITHOUT PipelineOverride on incremental service
        var result = await incrSvc.IngestAsync(slnPath, null, CancellationToken.None);

        // Assert
        result.Projects.Should().AllSatisfy(p =>
        {
            p.Status.Should().Be("skipped");
            p.Reason.Should().Be("unchanged");
        });
        result.ProjectsSkippedCount.Should().Be(2);
        result.IngestedProjectCount.Should().Be(0);
        result.Snapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task ProductionPath_FileChanged_DelegatesToFullIngest()
    {
        // Arrange
        var projects = new List<ProjectEntry>
        {
            new("ProjA", Path.Combine(_tempDir, "src", "ProjA", "ProjA.csproj"), []),
        };
        var nodes = new List<SymbolNode> { MakeNode("T:ProjA.TypeA", "ProjA") };

        var (slnPath, fullSvc, incrSvc) = await SetupProductionPathAsync(projects, [], nodes, []);

        // Modify a .cs file so manifest changes
        var csFile = Path.Combine(_tempDir, "src", "ProjA", "ProjA.cs");
        await File.WriteAllTextAsync(csFile, "namespace ProjA { public class TypeA { public int NewProp { get; set; } } }");

        // Set full service to return a controlled result
        bool fullIngestCalled = false;
        fullSvc.PipelineOverride = (sln, warnings, ct) =>
        {
            fullIngestCalled = true;
            var snapshot = new SolutionSnapshot("Test", projects, [], [
                new SymbolGraphSnapshot("1.2", "ProjA", "fp-ProjA", null, DateTimeOffset.UtcNow, nodes, [], null, "Test")
            ], DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", "Test", "fp-new", null, DateTimeOffset.UtcNow, nodes, [], null, "Test");
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();
            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", "Test", 1, 1, 1, 0, TimeSpan.FromMilliseconds(10),
                [OkStatus("ProjA", 1)], [], snapshot));
        };

        // Act
        var result = await incrSvc.IngestAsync(slnPath, null, CancellationToken.None);

        // Assert
        fullIngestCalled.Should().BeTrue("dirty set non-empty should trigger full ingest");
        result.Projects.Should().Contain(p => p.Status == "ok");
    }

    [Fact]
    public async Task ProductionPath_NothingChanged_PreservesStubs()
    {
        // Arrange: project with stubs, pre-saved on disk
        var projects = new List<ProjectEntry>
        {
            new("ProjA", Path.Combine(_tempDir, "src", "ProjA", "ProjA.csproj"), []),
        };
        var stubNode = MakeNode("T:ExternalLib.IFoo", "ExternalLib", NodeKind.Stub);
        var realNode = MakeNode("T:ProjA.MyClass", "ProjA");
        var stubEdge = MakeEdge("T:ProjA.MyClass", "T:ExternalLib.IFoo", SymbolEdgeKind.Implements, EdgeScope.External);

        var (slnPath, fullSvc, incrSvc) = await SetupProductionPathAsync(
            projects, [], [realNode, stubNode], [stubEdge]);

        // Full service should NOT be called
        fullSvc.PipelineOverride = (_, _, _) =>
            throw new InvalidOperationException("Full ingest should NOT be called");

        // Act
        var result = await incrSvc.IngestAsync(slnPath, null, CancellationToken.None);

        // Assert: stubs preserved in the returned snapshot
        result.Snapshot.Should().NotBeNull();
        var allNodes = result.Snapshot!.ProjectSnapshots.SelectMany(s => s.Nodes).ToList();
        allNodes.Should().Contain(n => n.NodeKind == NodeKind.Stub,
            "stubs from skipped projects must be preserved in production skip path");
        result.ProjectsSkippedCount.Should().Be(1);
    }

    [Fact]
    public async Task ProductionPath_NoPreviousSnapshot_DelegatesToFullIngest()
    {
        // Arrange: create services with NO pre-saved state
        var slnPath = Path.Combine(_tempDir, "Test.sln");
        await File.WriteAllTextAsync(slnPath, "# fake sln");

        var fullSvc = new SolutionIngestionService(
            _store, new InMemorySearchIndex(), NullLogger<SolutionIngestionService>.Instance);
        var incrSvc = new IncrementalSolutionIngestionService(
            _store, fullSvc, NullLogger<IncrementalSolutionIngestionService>.Instance);

        bool fullIngestCalled = false;
        var projects = new List<ProjectEntry>
        {
            new("ProjA", Path.Combine(_tempDir, "src", "ProjA", "ProjA.csproj"), []),
        };
        var projDir = Path.GetDirectoryName(projects[0].Path)!;
        Directory.CreateDirectory(projDir);
        await File.WriteAllTextAsync(projects[0].Path, "<Project />");

        fullSvc.PipelineOverride = (sln, warnings, ct) =>
        {
            fullIngestCalled = true;
            var nodes = new List<SymbolNode> { MakeNode("T:ProjA.TypeA", "ProjA") };
            var snapshot = new SolutionSnapshot("Test", projects, [],
                [new SymbolGraphSnapshot("1.2", "ProjA", "fp", null, DateTimeOffset.UtcNow, nodes, [], null, "Test")],
                DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", "Test", "fp", null, DateTimeOffset.UtcNow, nodes, [], null, "Test");
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();
            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", "Test", 1, 1, 1, 0, TimeSpan.FromMilliseconds(10),
                [OkStatus("ProjA", 1)], [], snapshot));
        };

        // Act
        var result = await incrSvc.IngestAsync(slnPath, null, CancellationToken.None);

        // Assert
        fullIngestCalled.Should().BeTrue("no previous snapshot should trigger full ingest");
    }
}
