using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using FluentAssertions;
using Lucene.Net.Store;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for KnowledgeQueryService covering QURY-01, QURY-02, QURY-03.
/// Uses an in-memory setup: BM25SearchIndex with RAMDirectory + temp-directory SnapshotStore.
/// </summary>
public class KnowledgeQueryServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ---------- helpers -------------------------------------------------------

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "KnowledgeQueryServiceTests_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static SymbolNode MakeNode(
        string id,
        string displayName,
        SymbolKind kind = SymbolKind.Method,
        string? docSummary = null) =>
        new(
            Id: new SymbolId(id),
            Kind: kind,
            DisplayName: displayName,
            FullyQualifiedName: displayName,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: docSummary is null ? null : new DocComment(
                Summary: docSummary,
                Remarks: null,
                Params: new Dictionary<string, string>(),
                TypeParams: new Dictionary<string, string>(),
                Returns: null,
                Examples: [],
                Exceptions: [],
                SeeAlso: []),
            Span: null);

    private static SymbolGraphSnapshot MakeSnapshot(
        SymbolNode[] nodes,
        SymbolEdge[]? edges = null,
        string? fingerprint = null,
        DateTimeOffset? createdAt = null) =>
        new(
            SchemaVersion: "v1",
            ProjectName: "TestProject",
            SourceFingerprint: fingerprint ?? "fp1",
            ContentHash: null,
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: edges ?? Array.Empty<SymbolEdge>());

    /// <summary>
    /// Creates a KnowledgeQueryService backed by a RAMDirectory index and temp SnapshotStore.
    /// Indexes and saves the given snapshot so it can be queried.
    /// Returns the service plus the saved snapshot (with ContentHash set).
    /// </summary>
    private async Task<(KnowledgeQueryService service, BM25SearchIndex index, SymbolGraphSnapshot saved)>
        CreateServiceAsync(SymbolNode[] nodes, SymbolEdge[]? edges = null, string? fingerprint = null, DateTimeOffset? createdAt = null)
    {
        var snapshot = MakeSnapshot(nodes, edges, fingerprint, createdAt);
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var saved = await store.SaveAsync(snapshot);

        var ramDir = new RAMDirectory();
        var index = new BM25SearchIndex(ramDir);
        await index.IndexAsync(saved, CancellationToken.None);

        var service = new KnowledgeQueryService(index, store);
        return (service, index, saved);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------- SearchAsync tests ---------------------------------------------

    [Fact]
    public async Task SearchAsync_ReturnsRankedResults()
    {
        var nodes = new[]
        {
            MakeNode("M:Alpha", "Alpha"),
            MakeNode("M:Beta",  "Beta"),
            MakeNode("M:AlphaExtra", "AlphaExtra"),
        };
        var (svc, _, _) = await CreateServiceAsync(nodes);

        var result = await svc.SearchAsync("Alpha");

        result.Success.Should().BeTrue();
        var items = result.Value!.Payload;
        items.Should().NotBeEmpty();
        items.All(i => i.Score > 0).Should().BeTrue("all returned items must have a positive score");
        // Alpha / AlphaExtra should score above Beta for "Alpha" query
        items.Should().NotContain(i => i.Id == new SymbolId("M:Beta"),
            because: "'Beta' does not match 'Alpha' query");
    }

    [Fact]
    public async Task SearchAsync_FiltersBySymbolKind()
    {
        var nodes = new[]
        {
            MakeNode("T:FooClass",  "FooClass",  SymbolKind.Type),
            MakeNode("M:FooMethod", "FooMethod", SymbolKind.Method),
            MakeNode("T:FooOther",  "FooOther",  SymbolKind.Type),
        };
        var (svc, _, _) = await CreateServiceAsync(nodes);

        var result = await svc.SearchAsync("Foo", kindFilter: SymbolKind.Method);

        result.Success.Should().BeTrue();
        var items = result.Value!.Payload;
        items.Should().OnlyContain(i => i.Kind == SymbolKind.Method, "kindFilter=Method should exclude Type symbols");
        items.Should().Contain(i => i.Id == new SymbolId("M:FooMethod"));
    }

    [Fact]
    public async Task SearchAsync_PaginatesWithOffsetAndLimit()
    {
        // 5 symbols all with "Process" in the name — all should match "Process" query
        var nodes = new[]
        {
            MakeNode("M:ProcessAlpha",   "ProcessAlpha"),
            MakeNode("M:ProcessBeta",    "ProcessBeta"),
            MakeNode("M:ProcessGamma",   "ProcessGamma"),
            MakeNode("M:ProcessDelta",   "ProcessDelta"),
            MakeNode("M:ProcessEpsilon", "ProcessEpsilon"),
        };
        var (svc, _, _) = await CreateServiceAsync(nodes);

        // First page — expect all 5 matching results when no offset/limit
        var allResult = await svc.SearchAsync("Process", offset: 0, limit: 10);
        allResult.Success.Should().BeTrue();
        var allItems = allResult.Value!.Payload;
        allItems.Should().HaveCountGreaterOrEqualTo(2, "at least 2 items must match 'Process' query before pagination test is valid");

        // Second page — offset=1 limit=2
        var result = await svc.SearchAsync("Process", offset: 1, limit: 2);
        result.Success.Should().BeTrue();
        var items = result.Value!.Payload;
        items.Should().HaveCount(2, "limit=2 with multiple matches should return exactly 2 items");
    }

    [Fact]
    public async Task SearchAsync_ReturnsStaleFlag_WhenIndexOutOfDate()
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        // Save two snapshots; index the first
        var snap1 = await store.SaveAsync(MakeSnapshot(
            [MakeNode("M:X", "X")], createdAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
        var snap2 = await store.SaveAsync(MakeSnapshot(
            [MakeNode("M:X", "X"), MakeNode("M:Y", "Y")], createdAt: DateTimeOffset.UtcNow));

        var ramDir = new RAMDirectory();
        var index = new BM25SearchIndex(ramDir);
        await index.IndexAsync(snap1, CancellationToken.None);

        var svc = new KnowledgeQueryService(index, store);

        // Query without pinning — should use latest (snap2), but index was built for snap1
        var result = await svc.SearchAsync("X", snapshotVersion: snap1.ContentHash);

        result.Success.Should().BeTrue();
        result.Value!.IsStale.Should().BeTrue("querying with an older snapshot when a newer one exists should set IsStale=true");
    }

    [Fact]
    public async Task ResponseEnvelope_ContainsSnapshotVersionAndDuration()
    {
        var (svc, _, saved) = await CreateServiceAsync([MakeNode("M:Z", "Z")]);

        var result = await svc.SearchAsync("Z");

        result.Success.Should().BeTrue();
        var envelope = result.Value!;
        envelope.SnapshotVersion.Should().Be(saved.ContentHash, "envelope must carry snapshot ContentHash");
        envelope.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        envelope.QueryDuration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    // ---------- GetSymbolAsync tests ------------------------------------------

    [Fact]
    public async Task GetSymbolAsync_ReturnsDetailWithNavigationHints()
    {
        var parent = MakeNode("T:Parent", "Parent", SymbolKind.Type);
        var child1 = MakeNode("M:Child1", "Child1");
        var child2 = MakeNode("M:Child2", "Child2");
        var related = MakeNode("T:Related", "Related", SymbolKind.Type);

        var edges = new[]
        {
            new SymbolEdge(new SymbolId("T:Parent"), new SymbolId("M:Child1"), SymbolEdgeKind.Contains),
            new SymbolEdge(new SymbolId("T:Parent"), new SymbolId("M:Child2"), SymbolEdgeKind.Contains),
            new SymbolEdge(new SymbolId("T:Parent"), new SymbolId("T:Related"), SymbolEdgeKind.References),
        };
        var (svc, _, _) = await CreateServiceAsync(
            [parent, child1, child2, related], edges);

        var result = await svc.GetSymbolAsync(new SymbolId("T:Parent"));

        result.Success.Should().BeTrue();
        var detail = result.Value!.Payload;
        detail.Node.Id.Should().Be(new SymbolId("T:Parent"));
        detail.ChildIds.Should().Contain(new SymbolId("M:Child1"));
        detail.ChildIds.Should().Contain(new SymbolId("M:Child2"));
        detail.RelatedIds.Should().Contain(new SymbolId("T:Related"));
        detail.ParentId.Should().BeNull("T:Parent has no parent in the edges");
    }

    [Fact]
    public async Task GetSymbolAsync_PopulatesParentId()
    {
        var parent = MakeNode("T:Parent", "Parent", SymbolKind.Type);
        var child = MakeNode("M:Child", "Child");
        var edges = new[]
        {
            new SymbolEdge(new SymbolId("T:Parent"), new SymbolId("M:Child"), SymbolEdgeKind.Contains),
        };
        var (svc, _, _) = await CreateServiceAsync([parent, child], edges);

        var result = await svc.GetSymbolAsync(new SymbolId("M:Child"));

        result.Success.Should().BeTrue();
        result.Value!.Payload.ParentId.Should().Be(new SymbolId("T:Parent"));
    }

    [Fact]
    public async Task GetSymbolAsync_ReturnsNotFoundForMissingId()
    {
        var (svc, _, _) = await CreateServiceAsync([MakeNode("M:Exists", "Exists")]);

        var result = await svc.GetSymbolAsync(new SymbolId("M:DoesNotExist"));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(QueryErrorKind.NotFound);
    }

    // ---------- DiffAsync tests -----------------------------------------------

    /// <summary>
    /// Creates a second SnapshotStore and service for diff tests that need two separate snapshots.
    /// </summary>
    private async Task<(KnowledgeQueryService service, SymbolGraphSnapshot savedA, SymbolGraphSnapshot savedB)>
        CreateDiffServiceAsync(SymbolNode[] nodesA, SymbolNode[] nodesB)
    {
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);

        var savedA = await store.SaveAsync(MakeSnapshot(nodesA, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5)));
        var savedB = await store.SaveAsync(MakeSnapshot(nodesB, createdAt: DateTimeOffset.UtcNow));

        var ramDir = new RAMDirectory();
        var index = new BM25SearchIndex(ramDir);
        await index.IndexAsync(savedB, CancellationToken.None);

        var service = new KnowledgeQueryService(index, store);
        return (service, savedA, savedB);
    }

    [Fact]
    public async Task DiffAsync_DetectsAddedSymbols()
    {
        var nodeX = MakeNode("M:X", "X");
        var nodeY = MakeNode("M:Y", "Y");
        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeX], [nodeX, nodeY]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        var entries = result.Value!.Payload.Entries;
        entries.Should().Contain(e => e.Id == new SymbolId("M:Y") && e.ChangeKind == DiffChangeKind.Added);
        entries.Should().NotContain(e => e.Id == new SymbolId("M:X"));
    }

    [Fact]
    public async Task DiffAsync_DetectsRemovedSymbols()
    {
        var nodeX = MakeNode("M:X", "X");
        var nodeY = MakeNode("M:Y", "Y");
        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeX, nodeY], [nodeX]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        var entries = result.Value!.Payload.Entries;
        entries.Should().Contain(e => e.Id == new SymbolId("M:Y") && e.ChangeKind == DiffChangeKind.Removed);
        entries.Should().NotContain(e => e.Id == new SymbolId("M:X"));
    }

    [Fact]
    public async Task DiffAsync_DetectsModifiedSymbols()
    {
        var nodeA = MakeNode("M:X", "OldName");
        var nodeB = MakeNode("M:X", "NewName");
        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeA], [nodeB]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        var entries = result.Value!.Payload.Entries;
        var modified = entries.Should().ContainSingle(e => e.Id == new SymbolId("M:X") && e.ChangeKind == DiffChangeKind.Modified).Subject;
        modified.Summary.Should().Contain("OldName").And.Contain("NewName");
    }

    [Fact]
    public async Task DiffAsync_DetectsRenamesViaPreviousIds()
    {
        var nodeY = MakeNode("M:Y", "Y");

        // Node Z in snapshot B claims M:Y as its previous identity
        var nodeZ = new SymbolNode(
            Id: new SymbolId("M:Z"),
            Kind: SymbolKind.Method,
            DisplayName: "Z",
            FullyQualifiedName: "Z",
            PreviousIds: [new SymbolId("M:Y")],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null);

        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeY], [nodeZ]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        var entries = result.Value!.Payload.Entries;
        // Should see a Modified entry for M:Z (renamed from M:Y), not separate Remove+Add
        entries.Should().Contain(e => e.Id == new SymbolId("M:Z") && e.ChangeKind == DiffChangeKind.Modified && e.Summary.Contains("M:Y"));
        entries.Should().NotContain(e => e.ChangeKind == DiffChangeKind.Removed);
        entries.Should().NotContain(e => e.Id == new SymbolId("M:Z") && e.ChangeKind == DiffChangeKind.Added);
    }

    [Fact]
    public async Task DiffAsync_ReturnsErrorForMissingSnapshot()
    {
        var (svc, savedA, _) = await CreateDiffServiceAsync([MakeNode("M:X", "X")], [MakeNode("M:X", "X")]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef("nonexistent-hash"));

        result.Success.Should().BeFalse();
        result.Error.Should().Be(QueryErrorKind.SnapshotMissing);
    }

    [Fact]
    public async Task DiffAsync_ReturnsEmptyDiffForIdenticalSnapshots()
    {
        var (svc, savedA, _) = await CreateDiffServiceAsync([MakeNode("M:X", "X")], [MakeNode("M:X", "X")]);

        // Compare A to itself
        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedA.ContentHash!));

        result.Success.Should().BeTrue();
        result.Value!.Payload.Entries.Should().BeEmpty("identical snapshots produce no diff entries");
    }

    [Fact]
    public async Task DiffAsync_DetectsDocChanges()
    {
        var nodeA = MakeNode("M:X", "X", docSummary: "Original docs");
        var nodeB = MakeNode("M:X", "X", docSummary: "Updated docs");
        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeA], [nodeB]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        var entries = result.Value!.Payload.Entries;
        var modified = entries.Should().ContainSingle(e => e.Id == new SymbolId("M:X") && e.ChangeKind == DiffChangeKind.Modified).Subject;
        modified.Summary.Should().Contain("doc changed");
    }

    [Fact]
    public async Task DiffAsync_ResponseEnvelopeUsesSnapshotBVersion()
    {
        var nodeX = MakeNode("M:X", "X");
        var (svc, savedA, savedB) = await CreateDiffServiceAsync([nodeX], [nodeX]);

        var result = await svc.DiffAsync(new SnapshotRef(savedA.ContentHash!), new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        result.Value!.SnapshotVersion.Should().Be(savedB.ContentHash, "envelope SnapshotVersion must be snapshot B's ContentHash");
        result.Value!.QueryDuration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
