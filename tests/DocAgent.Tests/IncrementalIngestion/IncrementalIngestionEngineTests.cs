using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DocAgent.McpServer.Config;

namespace DocAgent.Tests.IncrementalIngestion;

/// <summary>
/// Unit tests for <see cref="IncrementalIngestionEngine"/>.
/// Uses BuildOverride to avoid real Roslyn compilation.
/// File system is isolated per test via a GUID temp directory.
/// </summary>
public class IncrementalIngestionEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _artifactsDir;
    private readonly SnapshotStore _store;

    // Project layout: two virtual projects, each with a cs file
    private readonly string _projectADir;
    private readonly string _projectBDir;
    private readonly string _projectAFile;  // .csproj path
    private readonly string _projectBFile;
    private readonly string _csFileA;       // .cs source file under project A
    private readonly string _csFileB;       // .cs source file under project B

    public IncrementalIngestionEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IncEngineTests_" + Guid.NewGuid().ToString("N"));
        _artifactsDir = Path.Combine(_tempDir, "artifacts");
        Directory.CreateDirectory(_artifactsDir);

        _store = new SnapshotStore(_artifactsDir);

        // Project A
        _projectADir = Path.Combine(_tempDir, "ProjectA");
        Directory.CreateDirectory(_projectADir);
        _projectAFile = Path.Combine(_projectADir, "ProjectA.csproj");
        File.WriteAllText(_projectAFile, "<Project />");
        _csFileA = Path.Combine(_projectADir, "ClassA.cs");
        File.WriteAllText(_csFileA, "namespace A { public class ClassA {} }");

        // Project B
        _projectBDir = Path.Combine(_tempDir, "ProjectB");
        Directory.CreateDirectory(_projectBDir);
        _projectBFile = Path.Combine(_projectBDir, "ProjectB.csproj");
        File.WriteAllText(_projectBFile, "<Project />");
        _csFileB = Path.Combine(_projectBDir, "ClassB.cs");
        File.WriteAllText(_csFileB, "namespace B { public class ClassB {} }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SymbolNode MakeNode(string id, string? filePath = null)
        => new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: id,
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: filePath is null ? null : new SourceSpan(filePath, 1, 1, 1, 10),
            ReturnType: null,
            Parameters: [],
            GenericConstraints: []);

    private static SymbolEdge MakeEdge(string from, string to)
        => new(new SymbolId(from), new SymbolId(to), SymbolEdgeKind.Contains);

    private ProjectInventory MakeBothProjectsInventory()
        => new(
            RootPath: _tempDir,
            SolutionFiles: [],
            ProjectFiles: [_projectAFile, _projectBFile],
            XmlDocFiles: []);

    private ProjectInventory MakeProjectAInventory()
        => new(
            RootPath: _tempDir,
            SolutionFiles: [],
            ProjectFiles: [_projectAFile],
            XmlDocFiles: []);

    private static DocInputSet EmptyDocs()
        => new(new Dictionary<string, string>());

    private IncrementalIngestionEngine MakeEngine(
        Func<ProjectInventory, DocInputSet, CancellationToken, Task<SymbolGraphSnapshot>> buildOverride)
    {
        var engine = new IncrementalIngestionEngine(
            builder: new NullBuilder(),
            store: _store,
            artifactsDir: _artifactsDir);
        engine.BuildOverride = buildOverride;
        return engine;
    }

    private SymbolGraphSnapshot MakeSnapshot(IReadOnlyList<SymbolNode> nodes, IReadOnlyList<SymbolEdge>? edges = null)
        => new(
            SchemaVersion: "1.0",
            ProjectName: "TestProject",
            SourceFingerprint: "fp",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: edges ?? []);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstRun_NoPreviousSnapshot_DoesFullIngestion()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);

        var engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([nodeA, nodeB])));

        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: null,
            forceFullReingestion: false,
            ct: CancellationToken.None);

        result.Nodes.Should().HaveCount(2);
        result.IngestionMetadata.Should().NotBeNull();
        result.IngestionMetadata!.WasFullReingestion.Should().BeTrue();
        result.IngestionMetadata.RunId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NoChanges_ReturnsPreviousSnapshotWithUpdatedMetadata()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);
        var previous = MakeSnapshot([nodeA, nodeB]);

        // First run to establish the manifest
        var buildCallCount = 0;
        var engine = MakeEngine((inv, docs, ct) =>
        {
            buildCallCount++;
            return Task.FromResult(MakeSnapshot([nodeA, nodeB]));
        });

        await engine.IngestAsync(
            MakeBothProjectsInventory(), EmptyDocs(), null, false, CancellationToken.None);

        buildCallCount = 0; // Reset

        // Second run with no file changes
        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: previous,
            forceFullReingestion: false,
            ct: CancellationToken.None);

        buildCallCount.Should().Be(0, "no files changed so builder should not be called");
        result.IngestionMetadata.Should().NotBeNull();
        result.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
        result.IngestionMetadata.FileChanges.Should().BeEmpty();
        result.Nodes.Should().BeEquivalentTo(previous.Nodes);
    }

    [Fact]
    public async Task ChangedFileInProjectA_ReParsesOnlyProjectA_PreservesProjectBSymbols()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);
        var previous = MakeSnapshot([nodeA, nodeB]);

        // Establish manifest via first run
        var engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([nodeA, nodeB])));

        await engine.IngestAsync(MakeBothProjectsInventory(), EmptyDocs(), null, false, CancellationToken.None);

        // Modify ProjectA's source file
        File.WriteAllText(_csFileA, "namespace A { public class ClassA { public int X; } }");

        var reparsedNodeA = MakeNode("A.ClassA_Updated", _csFileA);
        var capturedInventory = (ProjectInventory?)null;

        engine = MakeEngine((inv, docs, ct) =>
        {
            capturedInventory = inv;
            return Task.FromResult(MakeSnapshot([reparsedNodeA]));
        });

        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: previous,
            forceFullReingestion: false,
            ct: CancellationToken.None);

        // Only ProjectA should have been re-parsed
        capturedInventory.Should().NotBeNull();
        capturedInventory!.ProjectFiles.Should().ContainSingle()
            .Which.Should().Be(_projectAFile);

        // ProjectB's node should be preserved
        result.Nodes.Should().Contain(n => n.Id.Value == "B.ClassB");
        // New ProjectA node should be present
        result.Nodes.Should().Contain(n => n.Id.Value == "A.ClassA_Updated");
        // Old ProjectA node should be replaced
        result.Nodes.Should().NotContain(n => n.Id.Value == "A.ClassA");

        result.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
    }

    [Fact]
    public async Task ForceFullReingestion_ReParsesAllProjects()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);
        var previous = MakeSnapshot([nodeA, nodeB]);

        // Establish manifest
        var engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([nodeA, nodeB])));
        await engine.IngestAsync(MakeBothProjectsInventory(), EmptyDocs(), null, false, CancellationToken.None);

        var capturedInventory = (ProjectInventory?)null;
        engine = MakeEngine((inv, docs, ct) =>
        {
            capturedInventory = inv;
            return Task.FromResult(MakeSnapshot([nodeA, nodeB]));
        });

        await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: previous,
            forceFullReingestion: true,
            ct: CancellationToken.None);

        capturedInventory.Should().NotBeNull();
        capturedInventory!.ProjectFiles.Should().HaveCount(2, "force re-ingestion should pass all projects");

        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(), EmptyDocs(), previous, true, CancellationToken.None);
        result.IngestionMetadata!.WasFullReingestion.Should().BeTrue();
    }

    [Fact]
    public async Task RemovedFile_SymbolsNotInOutput()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeA2 = MakeNode("A.ClassA2", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);
        var previous = MakeSnapshot([nodeA, nodeA2, nodeB]);

        // Establish manifest
        var engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([nodeA, nodeA2, nodeB])));
        await engine.IngestAsync(MakeBothProjectsInventory(), EmptyDocs(), null, false, CancellationToken.None);

        // Remove the ProjectA cs file entirely
        File.Delete(_csFileA);

        // After deletion, ProjectA dir has no .cs files, but manifest diff will show csFileA as removed
        // Re-parse ProjectA returns empty
        engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([])));

        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: previous,
            forceFullReingestion: false,
            ct: CancellationToken.None);

        result.Nodes.Should().NotContain(n => n.Id.Value == "A.ClassA");
        result.Nodes.Should().NotContain(n => n.Id.Value == "A.ClassA2");
        result.Nodes.Should().Contain(n => n.Id.Value == "B.ClassB");

        result.IngestionMetadata!.FileChanges
            .Should().Contain(fc => fc.FilePath == _csFileA && fc.ChangeKind == FileChangeKind.Removed);
    }

    [Fact]
    public async Task IngestionMetadata_CorrectlyRecordsFileChanges()
    {
        var nodeA = MakeNode("A.ClassA", _csFileA);
        var nodeB = MakeNode("B.ClassB", _csFileB);
        var previous = MakeSnapshot([nodeA, nodeB]);

        // Establish manifest
        var engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([nodeA, nodeB])));
        await engine.IngestAsync(MakeBothProjectsInventory(), EmptyDocs(), null, false, CancellationToken.None);

        // Modify ProjectA cs file
        File.WriteAllText(_csFileA, "namespace A { public class ClassA { public string Name; } }");

        var updatedNodeA = MakeNode("A.ClassA", _csFileA);
        engine = MakeEngine((inv, docs, ct) =>
            Task.FromResult(MakeSnapshot([updatedNodeA])));

        var result = await engine.IngestAsync(
            MakeBothProjectsInventory(),
            EmptyDocs(),
            previousSnapshot: previous,
            forceFullReingestion: false,
            ct: CancellationToken.None);

        var meta = result.IngestionMetadata;
        meta.Should().NotBeNull();
        meta!.RunId.Should().NotBeNullOrEmpty();
        meta.StartedAt.Should().BeOnOrBefore(meta.CompletedAt);
        meta.WasFullReingestion.Should().BeFalse();

        var modifiedRecord = meta.FileChanges.Should().Contain(fc =>
            fc.FilePath == _csFileA && fc.ChangeKind == FileChangeKind.Modified).Subject;

        modifiedRecord.AffectedSymbolIds.Should().Contain("A.ClassA");
    }

    // ── Null builder (never invoked directly when BuildOverride is set) ───────

    private sealed class NullBuilder : ISymbolGraphBuilder
    {
        public Task<SymbolGraphSnapshot> BuildAsync(ProjectInventory inv, DocInputSet docs, CancellationToken ct)
            => throw new InvalidOperationException("NullBuilder should never be called when BuildOverride is set.");
    }
}
