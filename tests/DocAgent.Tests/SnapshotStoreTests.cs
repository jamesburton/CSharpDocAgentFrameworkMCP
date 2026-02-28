using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

public class SnapshotStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SnapshotStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static SymbolGraphSnapshot BuildTestSnapshot(
        string projectName = "TestProject",
        string? fingerprint = null,
        DateTimeOffset? createdAt = null)
    {
        var node1 = new SymbolNode(
            Id: new SymbolId("TestProject.MyClass"),
            Kind: SymbolKind.Type,
            DisplayName: "MyClass",
            FullyQualifiedName: "TestProject.MyClass",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: new DocComment(
                Summary: "A test class.",
                Remarks: null,
                Params: new Dictionary<string, string>(),
                TypeParams: new Dictionary<string, string>(),
                Returns: null,
                Examples: [],
                Exceptions: [],
                SeeAlso: []),
            Span: new SourceSpan("src/MyClass.cs", 1, 0, 10, 0),
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

        var node2 = new SymbolNode(
            Id: new SymbolId("TestProject.MyClass.Method"),
            Kind: SymbolKind.Method,
            DisplayName: "Method",
            FullyQualifiedName: "TestProject.MyClass.Method",
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

        var edge = new SymbolEdge(
            From: new SymbolId("TestProject.MyClass"),
            To: new SymbolId("TestProject.MyClass.Method"),
            Kind: SymbolEdgeKind.Contains);

        return new SymbolGraphSnapshot(
            SchemaVersion: "1.0",
            ProjectName: projectName,
            SourceFingerprint: fingerprint ?? "fp_abc123",
            ContentHash: null,
            CreatedAt: createdAt ?? new DateTimeOffset(2026, 2, 26, 12, 0, 0, TimeSpan.Zero),
            Nodes: [node1, node2],
            Edges: [edge]);
    }

    [Fact]
    public async Task SaveAsync_writes_msgpack_file()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        var result = await store.SaveAsync(BuildTestSnapshot());

        result.ContentHash.Should().NotBeNullOrEmpty();
        var expectedFile = Path.Combine(dir, $"{result.ContentHash}.msgpack");
        File.Exists(expectedFile).Should().BeTrue("snapshot file should exist at {hash}.msgpack");
    }

    [Fact]
    public async Task SaveAsync_sets_content_hash()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        var result = await store.SaveAsync(BuildTestSnapshot());

        result.ContentHash.Should().NotBeNull("SaveAsync must set ContentHash on returned snapshot");
        result.ContentHash.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SaveAsync_updates_manifest()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var snapshot = BuildTestSnapshot("ManifestProject");

        var result = await store.SaveAsync(snapshot, gitCommitSha: "abc123def456");

        var manifestPath = Path.Combine(dir, "manifest.json");
        File.Exists(manifestPath).Should().BeTrue("manifest.json should be created");

        var entries = await store.ListAsync();
        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.ContentHash.Should().Be(result.ContentHash);
        entry.ProjectName.Should().Be("ManifestProject");
        entry.GitCommitSha.Should().Be("abc123def456");
        entry.SchemaVersion.Should().Be("1.0");
        entry.NodeCount.Should().Be(2);
        entry.EdgeCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_roundtrips_snapshot()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var original = BuildTestSnapshot("RoundtripProject");

        var saved = await store.SaveAsync(original);
        var loaded = await store.LoadAsync(saved.ContentHash!);

        loaded.Should().NotBeNull();
        loaded!.ProjectName.Should().Be(original.ProjectName);
        loaded.SchemaVersion.Should().Be(original.SchemaVersion);
        loaded.ContentHash.Should().Be(saved.ContentHash);
        loaded.Nodes.Should().HaveCount(original.Nodes.Count);
        loaded.Edges.Should().HaveCount(original.Edges.Count);
        loaded.Nodes[0].Id.Value.Should().Be(original.Nodes[0].Id.Value);
        loaded.Nodes[0].Docs!.Summary.Should().Be(original.Nodes[0].Docs!.Summary);
    }

    [Fact]
    public async Task LoadAsync_nonexistent_returns_null()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        var result = await store.LoadAsync("nonexistenthash00000000000000000");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_returns_all_entries()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        // Two snapshots with different project names → different content → different hashes
        var snapshot1 = BuildTestSnapshot("ProjectAlpha", fingerprint: "fp1");
        var snapshot2 = BuildTestSnapshot("ProjectBeta", fingerprint: "fp2");

        await store.SaveAsync(snapshot1);
        await store.SaveAsync(snapshot2);

        var entries = await store.ListAsync();
        entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_multiple_snapshots_coexist()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        var saved1 = await store.SaveAsync(BuildTestSnapshot("Alpha", fingerprint: "fp1"));
        var saved2 = await store.SaveAsync(BuildTestSnapshot("Beta", fingerprint: "fp2"));

        saved1.ContentHash.Should().NotBe(saved2.ContentHash, "different content should produce different hashes");

        File.Exists(Path.Combine(dir, $"{saved1.ContentHash}.msgpack")).Should().BeTrue();
        File.Exists(Path.Combine(dir, $"{saved2.ContentHash}.msgpack")).Should().BeTrue();

        var entries = await store.ListAsync();
        entries.Should().HaveCount(2);
        entries.Select(e => e.ContentHash).Should().Contain(saved1.ContentHash);
        entries.Select(e => e.ContentHash).Should().Contain(saved2.ContentHash);
    }

    [Fact]
    public async Task SaveAsync_deterministic_hash()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var fixedTime = new DateTimeOffset(2026, 2, 26, 12, 0, 0, TimeSpan.Zero);
        var snapshot = BuildTestSnapshot("DeterministicProject", createdAt: fixedTime);

        var result1 = await store.SaveAsync(snapshot);
        var result2 = await store.SaveAsync(snapshot);

        result1.ContentHash.Should().Be(result2.ContentHash, "same input must produce same hash");
        // Only one file should exist (same hash → same filename)
        var files = Directory.GetFiles(dir, "*.msgpack");
        files.Should().HaveCount(1);
        // Manifest should also have only one entry (duplicate replaced)
        var entries = await store.ListAsync();
        entries.Should().HaveCount(1);
    }
}
