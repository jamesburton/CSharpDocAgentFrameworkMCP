using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Tests.IncrementalIngestion;

/// <summary>
/// Integration tests that verify the hard correctness invariant:
/// incremental snapshot == full re-ingestion (after normalizing timestamps and per-run metadata).
///
/// Approach: Uses a <see cref="ContentHashedBuilder"/> that derives deterministic SymbolNodes
/// from actual file content (file path + SHA-256 of content), avoiding the need for real
/// MSBuild/Roslyn compilation while still exercising the incremental merge logic end-to-end.
///
/// This fallback approach is documented in the plan:
/// "If real Roslyn compilation in tests is too fragile, use the BuildOverride seam to inject
/// a deterministic mock builder that returns predictable nodes based on file content."
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public sealed class IncrementalCorrectnessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _artifactsDir;
    private readonly SnapshotStore _store;
    private readonly string _projectDir;
    private readonly string _projectFile;

    private static readonly MessagePackSerializerOptions SerializerOptions =
        ContractlessStandardResolver.Options;

    public IncrementalCorrectnessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IncrementalCorrectness_" + Guid.NewGuid().ToString("N"));
        _artifactsDir = Path.Combine(_tempDir, "artifacts");
        Directory.CreateDirectory(_artifactsDir);
        _store = new SnapshotStore(_artifactsDir);

        // Single project directory
        _projectDir = Path.Combine(_tempDir, "TestProject");
        Directory.CreateDirectory(_projectDir);
        _projectFile = Path.Combine(_projectDir, "TestProject.csproj");
        File.WriteAllText(_projectFile, "<Project />");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ProjectInventory MakeInventory()
        => new(
            RootPath: _tempDir,
            SolutionFiles: [],
            ProjectFiles: [_projectFile],
            XmlDocFiles: []);

    private static DocInputSet EmptyDocs()
        => new(new Dictionary<string, string>());

    /// <summary>
    /// Normalizes fields that legitimately differ between runs (timestamps, run IDs, content hashes).
    /// The normalized snapshot can be byte-compared.
    /// </summary>
    private static SymbolGraphSnapshot Normalize(SymbolGraphSnapshot s)
        => s with { CreatedAt = default, ContentHash = null, IngestionMetadata = null };

    private static byte[] Serialize(SymbolGraphSnapshot s)
        => MessagePackSerializer.Serialize(Normalize(s), SerializerOptions);

    /// <summary>
    /// Creates a fresh engine + artifacts directory for each "run" to simulate independent executions.
    /// The fresh engine has NO previous manifest, matching a full re-ingestion scenario.
    /// </summary>
    private IncrementalIngestionEngine MakeEngine(string artifactsDir)
        => new IncrementalIngestionEngine(
            builder: new ContentHashedBuilder(),
            store: _store,
            artifactsDir: artifactsDir);

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1: Ingest full (baseline), then ingest incrementally with identical files.
    /// After normalization, both snapshots must be byte-identical.
    /// </summary>
    [Fact]
    public async Task Incremental_with_no_changes_produces_identical_snapshot()
    {
        // Arrange: two .cs files
        var fileA = Path.Combine(_projectDir, "ClassA.cs");
        var fileB = Path.Combine(_projectDir, "ClassB.cs");
        File.WriteAllText(fileA, "namespace T { public class ClassA {} }");
        File.WriteAllText(fileB, "namespace T { public class ClassB {} }");

        var inventory = MakeInventory();

        // Act: full ingestion (no previous manifest, no previous snapshot)
        var engine1 = MakeEngine(_artifactsDir);
        var fullSnapshot = await engine1.IngestAsync(
            inventory, EmptyDocs(), previousSnapshot: null, forceFullReingestion: false,
            ct: CancellationToken.None);

        // Act: incremental with same files — manifest exists, no file changes
        var engine2 = MakeEngine(_artifactsDir);
        var incrementalSnapshot = await engine2.IngestAsync(
            inventory, EmptyDocs(), previousSnapshot: fullSnapshot, forceFullReingestion: false,
            ct: CancellationToken.None);

        // Assert: byte-identical after normalization
        var fullBytes = Serialize(fullSnapshot);
        var incrementalBytes = Serialize(incrementalSnapshot);

        fullBytes.SequenceEqual(incrementalBytes).Should().BeTrue(
            "incremental snapshot with no file changes must be byte-identical to the full snapshot after normalizing timestamps/metadata");

        // Sanity: nodes should be non-empty (builder produced content from files)
        fullSnapshot.Nodes.Should().HaveCount(2);
        incrementalSnapshot.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
        incrementalSnapshot.IngestionMetadata.FileChanges.Should().BeEmpty();
    }

    /// <summary>
    /// Test 2: Ingest full (baseline). Modify one file. Run incremental. Run full again.
    /// Incremental vs second full must be byte-identical after normalization.
    /// </summary>
    [Fact]
    public async Task Incremental_after_file_modification_matches_full_reingestion()
    {
        // Arrange: two .cs files
        var fileA = Path.Combine(_projectDir, "ClassA.cs");
        var fileB = Path.Combine(_projectDir, "ClassB.cs");
        File.WriteAllText(fileA, "namespace T { public class ClassA {} }");
        File.WriteAllText(fileB, "namespace T { public class ClassB {} }");

        var inventory = MakeInventory();

        // First full ingestion — establishes the manifest
        var engine1 = MakeEngine(_artifactsDir);
        var snapshot1 = await engine1.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        // Modify ClassA.cs (adds a method, changes content hash)
        File.WriteAllText(fileA, "namespace T { public class ClassA { public int X; } }");

        // Run incremental — only ClassA.cs changed, engine re-parses project, merges ClassB symbols
        var engine2 = MakeEngine(_artifactsDir);
        var incrementalSnapshot = await engine2.IngestAsync(
            inventory, EmptyDocs(), snapshot1, false, CancellationToken.None);

        // Run full from scratch on the modified state — fresh artifacts dir to reset manifest
        var freshArtifactsDir = Path.Combine(_tempDir, "artifacts_full2");
        Directory.CreateDirectory(freshArtifactsDir);
        var engine3 = MakeEngine(freshArtifactsDir);
        var fullSnapshot2 = await engine3.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        // Assert: incremental result matches full re-ingestion on the same modified files
        var incrementalBytes = Serialize(incrementalSnapshot);
        var fullBytes2 = Serialize(fullSnapshot2);

        incrementalBytes.SequenceEqual(fullBytes2).Should().BeTrue(
            "incremental snapshot after file modification must be byte-identical to a full re-ingestion on the same files, after normalizing timestamps/metadata");

        incrementalSnapshot.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
        incrementalSnapshot.Nodes.Should().HaveCount(2, "both ClassA and ClassB nodes must be present");
    }

    /// <summary>
    /// Test 3: Ingest full (baseline). Add a new .cs file. Run incremental. Run full again.
    /// Incremental vs full must be byte-identical after normalization.
    /// </summary>
    [Fact]
    public async Task Incremental_after_file_addition_matches_full_reingestion()
    {
        // Arrange: start with one .cs file
        var fileA = Path.Combine(_projectDir, "ClassA.cs");
        File.WriteAllText(fileA, "namespace T { public class ClassA {} }");

        var inventory = MakeInventory();

        // First full ingestion — establishes the manifest with one file
        var engine1 = MakeEngine(_artifactsDir);
        var snapshot1 = await engine1.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        snapshot1.Nodes.Should().HaveCount(1, "baseline should contain only ClassA");

        // Add a new file
        var fileB = Path.Combine(_projectDir, "ClassB.cs");
        File.WriteAllText(fileB, "namespace T { public class ClassB {} }");

        // Run incremental — ClassB.cs is new, engine re-parses project
        var engine2 = MakeEngine(_artifactsDir);
        var incrementalSnapshot = await engine2.IngestAsync(
            inventory, EmptyDocs(), snapshot1, false, CancellationToken.None);

        // Run full from scratch on the two-file state
        var freshArtifactsDir = Path.Combine(_tempDir, "artifacts_full2");
        Directory.CreateDirectory(freshArtifactsDir);
        var engine3 = MakeEngine(freshArtifactsDir);
        var fullSnapshot2 = await engine3.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        // Assert: byte-identical after normalization
        var incrementalBytes = Serialize(incrementalSnapshot);
        var fullBytes2 = Serialize(fullSnapshot2);

        incrementalBytes.SequenceEqual(fullBytes2).Should().BeTrue(
            "incremental snapshot after file addition must be byte-identical to a full re-ingestion on the same files");

        incrementalSnapshot.Nodes.Should().HaveCount(2, "ClassA and ClassB must both be present");
        incrementalSnapshot.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
        incrementalSnapshot.IngestionMetadata.FileChanges
            .Should().Contain(fc => fc.ChangeKind == FileChangeKind.Added,
                "the new file must be recorded as added in metadata");
    }

    /// <summary>
    /// Test 4: Ingest full (baseline). Delete a .cs file. Run incremental. Run full again.
    /// Incremental vs full must be byte-identical after normalization.
    /// </summary>
    [Fact]
    public async Task Incremental_after_file_removal_matches_full_reingestion()
    {
        // Arrange: two .cs files
        var fileA = Path.Combine(_projectDir, "ClassA.cs");
        var fileB = Path.Combine(_projectDir, "ClassB.cs");
        File.WriteAllText(fileA, "namespace T { public class ClassA {} }");
        File.WriteAllText(fileB, "namespace T { public class ClassB {} }");

        var inventory = MakeInventory();

        // First full ingestion — establishes the manifest with two files
        var engine1 = MakeEngine(_artifactsDir);
        var snapshot1 = await engine1.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        snapshot1.Nodes.Should().HaveCount(2);

        // Remove ClassB.cs
        File.Delete(fileB);

        // Run incremental — ClassB.cs is removed, engine re-parses project
        var engine2 = MakeEngine(_artifactsDir);
        var incrementalSnapshot = await engine2.IngestAsync(
            inventory, EmptyDocs(), snapshot1, false, CancellationToken.None);

        // Run full from scratch on the single-file state
        var freshArtifactsDir = Path.Combine(_tempDir, "artifacts_full2");
        Directory.CreateDirectory(freshArtifactsDir);
        var engine3 = MakeEngine(freshArtifactsDir);
        var fullSnapshot2 = await engine3.IngestAsync(
            inventory, EmptyDocs(), null, false, CancellationToken.None);

        // Assert: byte-identical after normalization
        var incrementalBytes = Serialize(incrementalSnapshot);
        var fullBytes2 = Serialize(fullSnapshot2);

        incrementalBytes.SequenceEqual(fullBytes2).Should().BeTrue(
            "incremental snapshot after file removal must be byte-identical to a full re-ingestion on the same files");

        incrementalSnapshot.Nodes.Should().HaveCount(1, "only ClassA must remain after ClassB removal");
        incrementalSnapshot.IngestionMetadata!.WasFullReingestion.Should().BeFalse();
        incrementalSnapshot.IngestionMetadata.FileChanges
            .Should().Contain(fc => fc.ChangeKind == FileChangeKind.Removed,
                "the removed file must be recorded as removed in metadata");
    }

    // ── ContentHashedBuilder ──────────────────────────────────────────────────

    /// <summary>
    /// A deterministic ISymbolGraphBuilder that produces one SymbolNode per .cs file found
    /// under each project directory. The node's ID is derived from the file's relative path and
    /// content hash — so it's stable across calls on identical content but changes when content changes.
    ///
    /// This avoids MSBuild/Roslyn SDK resolution complexity in tests while still exercising
    /// the incremental engine's manifest diff, partial re-parse, and symbol merge logic.
    /// </summary>
    private sealed class ContentHashedBuilder : ISymbolGraphBuilder
    {
        public async Task<SymbolGraphSnapshot> BuildAsync(
            ProjectInventory inv, DocInputSet docs, CancellationToken ct)
        {
            var nodes = new List<SymbolNode>();

            foreach (var projectFile in inv.ProjectFiles)
            {
                var projectDir = Path.GetDirectoryName(projectFile);
                if (projectDir is null || !Directory.Exists(projectDir))
                    continue;

                var csFiles = Directory
                    .EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal);

                foreach (var csFile in csFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = await File.ReadAllBytesAsync(csFile, ct).ConfigureAwait(false);
                    var hash = Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(content));

                    // Stable ID: relative path from project dir + content hash ensures
                    // the node ID changes when file content changes (simulating re-parse)
                    var relPath = Path.GetRelativePath(projectDir, csFile)
                        .Replace('\\', '/');
                    var symbolId = $"{relPath}#{hash[..8]}";

                    nodes.Add(new SymbolNode(
                        Id: new SymbolId(symbolId),
                        Kind: SymbolKind.Type,
                        DisplayName: Path.GetFileNameWithoutExtension(csFile),
                        FullyQualifiedName: symbolId,
                        PreviousIds: [],
                        Accessibility: Accessibility.Public,
                        Docs: null,
                        Span: new SourceSpan(csFile, 1, 1, 1, 10),
                        ReturnType: null,
                        Parameters: [],
                        GenericConstraints: []));
                }
            }

            var projectName = inv.ProjectFiles.Count > 0
                ? Path.GetFileNameWithoutExtension(inv.ProjectFiles[0])
                : "TestProject";

            return new SymbolGraphSnapshot(
                SchemaVersion: "1.0",
                ProjectName: projectName,
                SourceFingerprint: "test",
                ContentHash: null,
                CreatedAt: DateTimeOffset.UtcNow,
                Nodes: SymbolSorter.SortNodes(nodes),
                Edges: []);
        }
    }
}
