using DocAgent.Core;
using DocAgent.McpServer;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.Tests;

/// <summary>
/// End-to-end integration tests proving the full ingestion pipeline works: ingest a real
/// .NET project via IIngestionService, then query it via IKnowledgeQueryService.
/// Validates INGS-06: ingest_project trigger → discover → parse → snapshot → index → query.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IngestAndQueryE2ETests : IDisposable
{
    private readonly string _artifactsDir;

    // Use DocAgent.Core as the real project to ingest — it already builds and has XML docs.
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string CoreCsproj =>
        Path.Combine(RepoRoot, "src", "DocAgent.Core", "DocAgent.Core.csproj");

    public IngestAndQueryE2ETests()
    {
        _artifactsDir = Path.Combine(Path.GetTempPath(), $"E2EIngestQuery_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_artifactsDir, recursive: true); } catch { /* best effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DocAgentServerOptions>(o =>
        {
            o.ArtifactsDir = _artifactsDir;
            o.IngestionTimeoutSeconds = 120;
        });
        services.AddDocAgent();
        return services.BuildServiceProvider();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ingests DocAgent.Core (a real .NET project with XML docs), then searches by symbol name.
    /// Validates the full pipeline: discover → parse → snapshot → index → search.
    /// </summary>
    [Fact]
    public async Task IngestProject_ThenSearchSymbols_ReturnsResults()
    {
        await using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IIngestionService>();

        // Ingest the real DocAgent.Core project.
        var result = await svc.IngestAsync(
            path: CoreCsproj,
            includeGlob: null,
            excludeGlob: null,
            forceReindex: false,
            reportProgress: null,
            cancellationToken: CancellationToken.None);

        // Ingestion should succeed with symbols.
        result.SymbolCount.Should().BeGreaterThan(0, "DocAgent.Core has many public symbols");
        result.ProjectCount.Should().Be(1);
        result.IndexError.Should().BeNull("BM25 indexing should succeed");
        result.SnapshotId.Should().NotBeNullOrWhiteSpace();

        // Query for a well-known symbol in DocAgent.Core.
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        var searchResult = await query.SearchAsync("SymbolNode");

        searchResult.Success.Should().BeTrue();
        searchResult.Value!.Payload.Should().NotBeEmpty("SymbolNode is a well-known type in DocAgent.Core");
        searchResult.Value.Payload
            .Should().Contain(item => item.DisplayName.Contains("SymbolNode"),
                "search for 'SymbolNode' should return at least one matching result");
    }

    /// <summary>
    /// Ingests DocAgent.Core, then searches by a term from XML doc comment text.
    /// Validates BM25 doc-field search works after real ingestion.
    /// </summary>
    [Fact]
    public async Task IngestProject_ThenSearchByDocComment_ReturnsResults()
    {
        await using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IIngestionService>();

        var result = await svc.IngestAsync(
            path: CoreCsproj,
            includeGlob: null,
            excludeGlob: null,
            forceReindex: false,
            reportProgress: null,
            cancellationToken: CancellationToken.None);

        result.SymbolCount.Should().BeGreaterThan(0);
        result.IndexError.Should().BeNull();

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        // DocAgent.Core has types documented with terms like "snapshot" and "symbol".
        var searchResult = await query.SearchAsync("snapshot");

        searchResult.Success.Should().BeTrue();
        searchResult.Value!.Payload.Should().NotBeEmpty(
            "searching for 'snapshot' should match symbols/docs mentioning the snapshot concept");
    }

    /// <summary>
    /// Ingests the same project twice. The second ingestion should produce a valid result
    /// (idempotent) and not throw.
    /// </summary>
    [Fact]
    public async Task IngestProject_Twice_IsIdempotent()
    {
        await using var provider = BuildProvider();
        var svc = provider.GetRequiredService<IIngestionService>();

        var result1 = await svc.IngestAsync(CoreCsproj, null, null, false, null, CancellationToken.None);
        var result2 = await svc.IngestAsync(CoreCsproj, null, null, false, null, CancellationToken.None);

        result1.SymbolCount.Should().BeGreaterThan(0);
        result2.SymbolCount.Should().Be(result1.SymbolCount,
            "second ingestion of the same unchanged project should produce same symbol count");
        result2.IndexError.Should().BeNull();
    }
}
