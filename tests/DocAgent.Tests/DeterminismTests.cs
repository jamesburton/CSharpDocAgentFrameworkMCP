using DocAgent.Core;
using DocAgent.Ingestion;
using FluentAssertions;
using MessagePack;
using MessagePack.Resolvers;

namespace DocAgent.Tests;

/// <summary>
/// End-to-end determinism tests that prove the full ingestion pipeline produces
/// byte-identical SymbolGraphSnapshot artifacts across independent runs on the same input.
/// Determinism is a core project requirement: same input must always yield identical output.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DeterminismTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CoreCsproj =>
        Path.Combine(RepoRoot, "src", "DocAgent.Core", "DocAgent.Core.csproj");

    private static string SolutionPath =>
        Path.Combine(RepoRoot, "src", "DocAgentFramework.sln");

    private static readonly DateTimeOffset FixedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly MessagePackSerializerOptions SerializerOptions =
        ContractlessStandardResolver.Options;

    private static RoslynSymbolGraphBuilder CreateBuilder()
    {
        var parser = new XmlDocParser();
        var resolver = new InheritDocResolver();
        return new RoslynSymbolGraphBuilder(parser, resolver, logWarning: null);
    }

    private static ProjectInventory CoreInventory()
        => new ProjectInventory(
            RootPath: Path.GetDirectoryName(CoreCsproj)!,
            SolutionFiles: [],
            ProjectFiles: [CoreCsproj],
            XmlDocFiles: []);

    private static readonly DocInputSet EmptyDocs =
        new DocInputSet(new Dictionary<string, string>());

    // ── Test 1: Primary end-to-end determinism test ──────────────────────────

    [Fact]
    public async Task FullPipeline_produces_identical_snapshots_across_runs()
    {
        var builder = CreateBuilder();

        var snapshot1 = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);
        var snapshot2 = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        // Fix CreatedAt to the same constant to eliminate wall-clock non-determinism
        var fixed1 = snapshot1 with { CreatedAt = FixedTimestamp };
        var fixed2 = snapshot2 with { CreatedAt = FixedTimestamp };

        var bytes1 = MessagePackSerializer.Serialize(fixed1, SerializerOptions);
        var bytes2 = MessagePackSerializer.Serialize(fixed2, SerializerOptions);

        bytes1.SequenceEqual(bytes2).Should().BeTrue(
            "two independent pipeline runs on the same project must produce byte-identical snapshots");
    }

    // ── Test 2: Content hash stable across runs via SnapshotStore ────────────

    [Fact]
    public async Task FullPipeline_content_hash_stable_across_runs()
    {
        var builder = CreateBuilder();

        var snapshot1 = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);
        var snapshot2 = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        // Fix timestamps so the serialized bytes are identical going into the store
        var fixed1 = snapshot1 with { CreatedAt = FixedTimestamp };
        var fixed2 = snapshot2 with { CreatedAt = FixedTimestamp };

        var dir1 = Path.Combine(Path.GetTempPath(), "DeterminismTest_" + Guid.NewGuid().ToString("N"));
        var dir2 = Path.Combine(Path.GetTempPath(), "DeterminismTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store1 = new SnapshotStore(dir1);
            var store2 = new SnapshotStore(dir2);

            var saved1 = await store1.SaveAsync(fixed1);
            var saved2 = await store2.SaveAsync(fixed2);

            saved1.ContentHash.Should().NotBeNull();
            saved2.ContentHash.Should().NotBeNull();
            saved1.ContentHash.Should().Be(saved2.ContentHash,
                "identical snapshot content must produce identical content hashes");

            // The .msgpack files on disk must also be byte-identical
            var file1Bytes = await File.ReadAllBytesAsync(Path.Combine(dir1, $"{saved1.ContentHash}.msgpack"));
            var file2Bytes = await File.ReadAllBytesAsync(Path.Combine(dir2, $"{saved2.ContentHash}.msgpack"));
            file1Bytes.SequenceEqual(file2Bytes).Should().BeTrue(
                "stored .msgpack files for identical snapshots must be byte-identical");
        }
        finally
        {
            try { Directory.Delete(dir1, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(dir2, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── Test 3: Determinism across multiple projects (catches ordering issues) ─

    [Fact]
    public async Task FullPipeline_deterministic_with_multiple_projects()
    {
        // Discover the full solution (excluding test projects)
        var source = new LocalProjectSource(includeTestProjects: false, logWarning: null);
        var locator = new ProjectLocator(SolutionPath);
        var inventory = await source.DiscoverAsync(locator, CancellationToken.None);

        inventory.ProjectFiles.Should().HaveCountGreaterThan(1,
            "solution should contain multiple non-test projects for this test to be meaningful");

        var builder = CreateBuilder();

        var snapshot1 = await builder.BuildAsync(inventory, EmptyDocs, CancellationToken.None);
        var snapshot2 = await builder.BuildAsync(inventory, EmptyDocs, CancellationToken.None);

        // Fix timestamps to eliminate wall-clock non-determinism
        var fixed1 = snapshot1 with { CreatedAt = FixedTimestamp };
        var fixed2 = snapshot2 with { CreatedAt = FixedTimestamp };

        var bytes1 = MessagePackSerializer.Serialize(fixed1, SerializerOptions);
        var bytes2 = MessagePackSerializer.Serialize(fixed2, SerializerOptions);

        bytes1.SequenceEqual(bytes2).Should().BeTrue(
            "two independent runs against a multi-project solution must produce byte-identical snapshots; " +
            "failures indicate non-deterministic ordering or dictionary iteration");
    }

    // ── Test 4: Nodes sorted by ordinal Id ───────────────────────────────────

    [Fact]
    public async Task Nodes_sorted_by_ordinal_id()
    {
        var builder = CreateBuilder();
        var snapshot = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var ids = snapshot.Nodes.Select(n => n.Id.Value).ToList();
        var sorted = ids.OrderBy(s => s, StringComparer.Ordinal).ToList();

        ids.Should().Equal(sorted,
            "nodes must be sorted in Ordinal order by Id.Value for deterministic snapshot serialization");
    }

    // ── Test 5: Edges sorted deterministically ────────────────────────────────

    [Fact]
    public async Task Edges_sorted_deterministically()
    {
        var builder = CreateBuilder();
        var snapshot = await builder.BuildAsync(CoreInventory(), EmptyDocs, CancellationToken.None);

        var edges = snapshot.Edges.ToList();
        var sorted = edges
            .OrderBy(e => e.From.Value, StringComparer.Ordinal)
            .ThenBy(e => e.To.Value, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ToList();

        for (int i = 0; i < edges.Count; i++)
        {
            edges[i].Should().Be(sorted[i],
                $"edge at index {i} must be in canonical order (From, To, Kind) for deterministic snapshots");
        }
    }
}
