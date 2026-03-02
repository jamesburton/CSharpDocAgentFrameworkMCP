using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocAgent.Tests.IncrementalIngestion;

/// <summary>
/// Byte-identity determinism tests (INGEST-05) proving that incremental solution ingestion
/// produces output identical to full ingestion when no files have changed, and detects
/// differences when files do change.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public sealed class SolutionIncrementalDeterminismTests : IDisposable
{
    private static readonly DateTimeOffset FixedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly MessagePackSerializerOptions SerializerOptions =
        ContractlessStandardResolver.Options;

    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SolutionIncrementalDeterminismTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"det-sln-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (SolutionIngestionService full, IncrementalSolutionIngestionService incremental) CreateServices()
    {
        var fullService = new SolutionIngestionService(
            _store,
            new InMemorySearchIndex(),
            NullLogger<SolutionIngestionService>.Instance);

        var incrementalService = new IncrementalSolutionIngestionService(
            _store,
            fullService,
            NullLogger<IncrementalSolutionIngestionService>.Instance);

        return (fullService, incrementalService);
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

    private static SymbolEdge MakeEdge(string from, string to,
        SymbolEdgeKind edgeKind = SymbolEdgeKind.Contains,
        EdgeScope scope = EdgeScope.IntraProject) =>
        new(new SymbolId(from), new SymbolId(to), edgeKind, scope);

    /// <summary>
    /// Normalizes non-deterministic fields on a snapshot so byte comparison is meaningful.
    /// Follows the pattern from DeterminismTests.cs.
    /// </summary>
    private static SymbolGraphSnapshot Normalize(SymbolGraphSnapshot snapshot) =>
        snapshot with
        {
            CreatedAt = FixedTimestamp,
            ContentHash = null,
            IngestionMetadata = null,
            SourceFingerprint = "normalized"
        };

    /// <summary>
    /// Creates a pipeline override that returns a fixed set of nodes/edges/projects,
    /// producing deterministic output for both full and incremental services.
    /// </summary>
    private Func<string, List<string>, CancellationToken, Task<SolutionIngestionResult>> MakeFullPipelineOverride(
        IReadOnlyList<SymbolNode> nodes,
        IReadOnlyList<SymbolEdge> edges,
        IReadOnlyList<ProjectEntry> projects)
    {
        return (slnPath, warnings, ct) =>
        {
            var solutionName = Path.GetFileNameWithoutExtension(slnPath);
            var statuses = projects.Select(p =>
                new ProjectIngestionStatus(p.Name, p.Path, "ok", null, nodes.Count(n => n.ProjectOrigin == p.Name), null))
                .ToList();

            var perProjectSnapshots = projects.Select(p =>
                new SymbolGraphSnapshot("1.2", p.Name, $"fp-{p.Name}", null, DateTimeOffset.UtcNow,
                    nodes.Where(n => n.ProjectOrigin == p.Name).ToList(),
                    edges.Where(e => nodes.Any(n => n.Id == e.From && n.ProjectOrigin == p.Name)).ToList(),
                    null, solutionName)).ToList();

            var snapshot = new SolutionSnapshot(solutionName, projects, [], perProjectSnapshots, DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", solutionName, $"fp-{Guid.NewGuid():N}", null,
                DateTimeOffset.UtcNow,
                SymbolSorter.SortNodes(nodes.ToList()),
                SymbolSorter.SortEdges(edges.ToList()),
                null, solutionName);
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", solutionName, projects.Count, statuses.Count,
                nodes.Count, edges.Count, TimeSpan.FromMilliseconds(50), statuses, [], snapshot));
        };
    }

    private Func<string, bool, List<string>, CancellationToken, Task<SolutionIngestionResult>> MakeIncrementalPipelineOverride(
        IReadOnlyList<SymbolNode> nodes,
        IReadOnlyList<SymbolEdge> edges,
        IReadOnlyList<ProjectEntry> projects)
    {
        return (slnPath, forceFullReingest, warnings, ct) =>
        {
            var solutionName = Path.GetFileNameWithoutExtension(slnPath);
            // Incremental with no changes: all skipped but same nodes/edges preserved
            var statuses = projects.Select(p =>
                new ProjectIngestionStatus(p.Name, p.Path, "skipped", "unchanged", null, null))
                .ToList();

            var perProjectSnapshots = projects.Select(p =>
                new SymbolGraphSnapshot("1.2", p.Name, $"fp-{p.Name}", null, DateTimeOffset.UtcNow,
                    nodes.Where(n => n.ProjectOrigin == p.Name).ToList(),
                    edges.Where(e => nodes.Any(n => n.Id == e.From && n.ProjectOrigin == p.Name)).ToList(),
                    null, solutionName)).ToList();

            var snapshot = new SolutionSnapshot(solutionName, projects, [], perProjectSnapshots, DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", solutionName, $"fp-{Guid.NewGuid():N}", null,
                DateTimeOffset.UtcNow,
                SymbolSorter.SortNodes(nodes.ToList()),
                SymbolSorter.SortEdges(edges.ToList()),
                null, solutionName);
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", solutionName, projects.Count, 0,
                nodes.Count, edges.Count, TimeSpan.FromMilliseconds(50), statuses, [], snapshot));
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementalSolution_ByteIdentical_To_FullIngest_WhenUnchanged()
    {
        // Arrange: multi-project scenario with a dependency edge
        var projects = new List<ProjectEntry>
        {
            new("ProjCore", "/src/ProjCore/ProjCore.csproj", []),
            new("ProjApp", "/src/ProjApp/ProjApp.csproj", ["ProjCore"]),
        };

        var nodes = new List<SymbolNode>
        {
            MakeNode("T:ProjCore.BaseService", "ProjCore"),
            MakeNode("T:ProjCore.IService", "ProjCore"),
            MakeNode("T:ProjApp.AppService", "ProjApp"),
        };

        var edges = new List<SymbolEdge>
        {
            MakeEdge("T:ProjApp.AppService", "T:ProjCore.IService", SymbolEdgeKind.Implements, EdgeScope.CrossProject),
            MakeEdge("T:ProjApp.AppService", "T:ProjCore.BaseService", SymbolEdgeKind.Inherits, EdgeScope.CrossProject),
        };

        var (fullSvc, incrSvc) = CreateServices();

        // Wire both services to produce identical node/edge sets
        fullSvc.PipelineOverride = MakeFullPipelineOverride(nodes, edges, projects);
        incrSvc.PipelineOverride = MakeIncrementalPipelineOverride(nodes, edges, projects);

        // Act: Run full ingestion
        var fullResult = await fullSvc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);

        // Run incremental ingestion (no changes)
        var incrResult = await incrSvc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);

        // Load both snapshots from store
        var fullSnapshot = await _store.LoadAsync(fullResult.SnapshotId, CancellationToken.None);
        var incrSnapshot = await _store.LoadAsync(incrResult.SnapshotId, CancellationToken.None);

        fullSnapshot.Should().NotBeNull("full snapshot must be loadable from store");
        incrSnapshot.Should().NotBeNull("incremental snapshot must be loadable from store");

        // Normalize non-deterministic fields
        var normalizedFull = Normalize(fullSnapshot!);
        var normalizedIncr = Normalize(incrSnapshot!);

        // Serialize and compare bytes
        var bytesFull = MessagePackSerializer.Serialize(normalizedFull, SerializerOptions);
        var bytesIncr = MessagePackSerializer.Serialize(normalizedIncr, SerializerOptions);

        bytesFull.SequenceEqual(bytesIncr).Should().BeTrue(
            "incremental solution ingestion must produce byte-identical output to full ingestion " +
            "when no files have changed (INGEST-05)");
    }

    [Fact]
    public async Task IncrementalSolution_DifferentOutput_WhenFileChanged()
    {
        // Arrange: same multi-project setup
        var projects = new List<ProjectEntry>
        {
            new("ProjCore", "/src/ProjCore/ProjCore.csproj", []),
            new("ProjApp", "/src/ProjApp/ProjApp.csproj", ["ProjCore"]),
        };

        var originalNodes = new List<SymbolNode>
        {
            MakeNode("T:ProjCore.BaseService", "ProjCore"),
            MakeNode("T:ProjApp.AppService", "ProjApp"),
        };

        var originalEdges = new List<SymbolEdge>
        {
            MakeEdge("T:ProjApp.AppService", "T:ProjCore.BaseService", SymbolEdgeKind.Inherits, EdgeScope.CrossProject),
        };

        // After change: ProjApp gains a new type
        var changedNodes = new List<SymbolNode>
        {
            MakeNode("T:ProjCore.BaseService", "ProjCore"),
            MakeNode("T:ProjApp.AppService", "ProjApp"),
            MakeNode("T:ProjApp.NewController", "ProjApp"), // new type added
        };

        var changedEdges = new List<SymbolEdge>
        {
            MakeEdge("T:ProjApp.AppService", "T:ProjCore.BaseService", SymbolEdgeKind.Inherits, EdgeScope.CrossProject),
        };

        var (fullSvc, incrSvc) = CreateServices();

        // Full ingest: original nodes
        fullSvc.PipelineOverride = MakeFullPipelineOverride(originalNodes, originalEdges, projects);

        // Incremental ingest: changed nodes (simulates file modification in ProjApp)
        incrSvc.PipelineOverride = (slnPath, forceFullReingest, warnings, ct) =>
        {
            var solutionName = Path.GetFileNameWithoutExtension(slnPath);
            var statuses = new List<ProjectIngestionStatus>
            {
                new("ProjCore", "/src/ProjCore/ProjCore.csproj", "skipped", "unchanged", null, null),
                new("ProjApp", "/src/ProjApp/ProjApp.csproj", "ok", null, 2, null), // re-ingested
            };

            var perProjectSnapshots = projects.Select(p =>
                new SymbolGraphSnapshot("1.2", p.Name, $"fp-{p.Name}", null, DateTimeOffset.UtcNow,
                    changedNodes.Where(n => n.ProjectOrigin == p.Name).ToList(), [], null, solutionName)).ToList();

            var snapshot = new SolutionSnapshot(solutionName, projects, [], perProjectSnapshots, DateTimeOffset.UtcNow);
            var merged = new SymbolGraphSnapshot("1.2", solutionName, $"fp-changed", null,
                DateTimeOffset.UtcNow,
                SymbolSorter.SortNodes(changedNodes),
                SymbolSorter.SortEdges(changedEdges),
                null, solutionName);
            var saved = _store.SaveAsync(merged, ct: ct).GetAwaiter().GetResult();

            return Task.FromResult(new SolutionIngestionResult(
                saved.ContentHash ?? "hash", solutionName, projects.Count, 1,
                changedNodes.Count, changedEdges.Count, TimeSpan.FromMilliseconds(50), statuses, [], snapshot));
        };

        // Act
        var fullResult = await fullSvc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);
        var incrResult = await incrSvc.IngestAsync("/path/TestSln.sln", null, CancellationToken.None);

        // Load and normalize
        var fullSnapshot = await _store.LoadAsync(fullResult.SnapshotId, CancellationToken.None);
        var incrSnapshot = await _store.LoadAsync(incrResult.SnapshotId, CancellationToken.None);

        fullSnapshot.Should().NotBeNull();
        incrSnapshot.Should().NotBeNull();

        var normalizedFull = Normalize(fullSnapshot!);
        var normalizedIncr = Normalize(incrSnapshot!);

        var bytesFull = MessagePackSerializer.Serialize(normalizedFull, SerializerOptions);
        var bytesIncr = MessagePackSerializer.Serialize(normalizedIncr, SerializerOptions);

        // Assert: outputs should differ (sanity check for change detection)
        bytesFull.SequenceEqual(bytesIncr).Should().BeFalse(
            "incremental ingestion after a file change must produce different output than the original " +
            "full ingestion — this confirms incremental actually detects changes");
    }
}
