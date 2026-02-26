using DocAgent.Core;
using DocAgent.Indexing;
using FluentAssertions;

namespace DocAgent.Tests;

/// <summary>
/// Filesystem-based integration tests for BM25SearchIndex persistence.
/// Each test uses a unique temp directory and cleans up in a finally block.
/// </summary>
public class BM25SearchIndexPersistenceTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string MakeTempDir()
        => Path.Combine(Path.GetTempPath(), $"bm25-test-{Guid.NewGuid()}");

    private static SymbolGraphSnapshot MakeSnapshot(string? contentHash, params string[] names)
    {
        var nodes = names.Select((n, i) => new SymbolNode(
            Id:                 new SymbolId($"id-{i}"),
            Kind:               SymbolKind.Type,
            DisplayName:        n,
            FullyQualifiedName: $"Namespace.{n}",
            PreviousIds:        [],
            Accessibility:      Accessibility.Public,
            Docs:               null,
            Span:               null)).ToArray();

        return new SymbolGraphSnapshot(
            SchemaVersion:      "v1",
            ProjectName:        "test",
            SourceFingerprint:  "fixture",
            ContentHash:        contentHash,
            CreatedAt:          DateTimeOffset.UtcNow,
            Nodes:              nodes,
            Edges:              Array.Empty<SymbolEdge>());
    }

    // ---------------------------------------------------------------------------
    // Test 1: IndexAsync creates the .lucene directory with segment files
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IndexAsync_creates_lucene_directory()
    {
        var tempDir = MakeTempDir();
        try
        {
            var snapshot = MakeSnapshot("testhash123", "Alpha", "Beta");

            using var index = new BM25SearchIndex(tempDir);
            await index.IndexAsync(snapshot, CancellationToken.None);

            var luceneDir = Path.Combine(tempDir, "testhash123.lucene");
            System.IO.Directory.Exists(luceneDir).Should().BeTrue("index directory should be created");

            var files = System.IO.Directory.GetFiles(luceneDir);
            files.Should().NotBeEmpty("Lucene segment files should be written");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 2: IndexAsync skips rebuild when index is fresh (same hash)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IndexAsync_skips_rebuild_when_fresh()
    {
        var tempDir = MakeTempDir();
        try
        {
            var snapshot = MakeSnapshot("freshhash", "Alpha");

            using var index = new BM25SearchIndex(tempDir);
            await index.IndexAsync(snapshot, CancellationToken.None);

            // Capture modification time of segment files before second call.
            var luceneDir = Path.Combine(tempDir, "freshhash.lucene");
            var timestampsBefore = System.IO.Directory.GetFiles(luceneDir)
                .ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

            // Second call with same hash — should be a no-op (freshness check).
            await index.IndexAsync(snapshot, CancellationToken.None);

            var timestampsAfter = System.IO.Directory.GetFiles(luceneDir)
                .ToDictionary(f => f, f => File.GetLastWriteTimeUtc(f));

            // All existing files should have unchanged timestamps.
            foreach (var (path, before) in timestampsBefore)
            {
                if (timestampsAfter.TryGetValue(path, out var after))
                    after.Should().Be(before, $"file '{Path.GetFileName(path)}' should not be rewritten on second call");
            }
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 3: IndexAsync creates separate directories for different hashes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IndexAsync_creates_separate_directories_for_different_hashes()
    {
        var tempDir = MakeTempDir();
        try
        {
            var snapshotA = MakeSnapshot("hashA", "Alpha");
            var snapshotB = MakeSnapshot("hashB", "Beta");

            using var indexA = new BM25SearchIndex(tempDir);
            await indexA.IndexAsync(snapshotA, CancellationToken.None);

            using var indexB = new BM25SearchIndex(tempDir);
            await indexB.IndexAsync(snapshotB, CancellationToken.None);

            System.IO.Directory.Exists(Path.Combine(tempDir, "hashA.lucene")).Should().BeTrue("hashA directory should exist");
            System.IO.Directory.Exists(Path.Combine(tempDir, "hashB.lucene")).Should().BeTrue("hashB directory should exist");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 4: IndexAsync throws ArgumentException when ContentHash is null
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IndexAsync_throws_on_null_content_hash()
    {
        var tempDir = MakeTempDir();
        try
        {
            // ContentHash = null — FSDirectory mode requires it.
            var snapshot = MakeSnapshot(null, "Alpha");

            using var index = new BM25SearchIndex(tempDir);
            var act = async () => await index.IndexAsync(snapshot, CancellationToken.None);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*ContentHash*");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 5: Persisted index is searchable after instance restart (LoadIndexAsync)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Persisted_index_searchable_after_reload()
    {
        var tempDir = MakeTempDir();
        try
        {
            const string hash = "reloadhash";
            var snapshot = MakeSnapshot(hash, "AlphaService", "BetaRepository");

            // First instance: index and dispose.
            using (var first = new BM25SearchIndex(tempDir))
            {
                await first.IndexAsync(snapshot, CancellationToken.None);
            }

            // Second instance: load without re-indexing.
            using var second = new BM25SearchIndex(tempDir);
            await second.LoadIndexAsync(hash, snapshot, CancellationToken.None);

            // GetAsync should work (populated from snapshot during LoadIndexAsync).
            var node = await second.GetAsync(new SymbolId("id-0"), CancellationToken.None);
            node.Should().NotBeNull("GetAsync should return the node after LoadIndexAsync");
            node!.DisplayName.Should().Be("AlphaService");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------------
    // Test 6: Full round-trip — index → dispose → new instance → search → verify
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Search_returns_results_from_persisted_index()
    {
        var tempDir = MakeTempDir();
        try
        {
            const string hash = "roundtrip";
            var snapshot = MakeSnapshot(hash, "UserRepository", "OrderService", "PaymentGateway");

            // Write index with first instance.
            using (var writer = new BM25SearchIndex(tempDir))
            {
                await writer.IndexAsync(snapshot, CancellationToken.None);
            }

            // New instance — freshness check should detect existing index and skip rebuild.
            using var reader = new BM25SearchIndex(tempDir);
            await reader.IndexAsync(snapshot, CancellationToken.None);

            var results = new List<SearchHit>();
            await foreach (var hit in reader.SearchAsync("Repository", CancellationToken.None))
                results.Add(hit);

            results.Should().NotBeEmpty("searching a persisted index should return results");
            results.Should().Contain(h => h.Snippet.Contains("UserRepository"),
                "UserRepository should match the 'Repository' query");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
                System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }
}
