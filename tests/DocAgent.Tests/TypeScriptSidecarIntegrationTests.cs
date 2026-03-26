using System.Text.Json;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Real sidecar E2E integration tests that exercise the full pipeline:
/// Node.js sidecar → JSON → C# deserialization → valid snapshot → queryable index.
///
/// These tests are always skipped in standard <c>dotnet test</c> runs via <c>[Fact(Skip=...)]</c>.
/// They require Node.js and the compiled sidecar (<c>ts-symbol-extractor/dist/index.js</c>).
/// Run them via IDE test runner after temporarily removing the Skip attribute.
///
/// Coverage: MCPI-01 (ingest_typescript), MCPI-02 (search_symbols, get_symbol, get_references).
/// </summary>
[Trait("Category", "Sidecar")]
public sealed class TypeScriptSidecarIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public TypeScriptSidecarIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptSidecarIntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Locate the simple-project fixture tsconfig.json by walking up from the test
    /// assembly directory to find the ts-symbol-extractor fixture folder.
    /// </summary>
    private static string? FindSimpleProjectTsconfig()
    {
        // Walk up from AppContext.BaseDirectory to find repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ts-symbol-extractor", "tests", "fixtures", "simple-project", "tsconfig.json");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private TypeScriptIngestionService CreateIngestionService(string tempDir)
    {
        var options = new DocAgentServerOptions
        {
            ArtifactsDir = tempDir,
            AllowedPaths = ["**"]
        };
        var optionsWrapper = Options.Create(options);
        var store = new SnapshotStore(tempDir);
        var index = new BM25SearchIndex(new LuceneRAMDirectory());
        var allowlist = new PathAllowlist(optionsWrapper);
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TypeScriptIngestionService>();
        return new TypeScriptIngestionService(store, index, allowlist, optionsWrapper, logger);
    }

    /// <summary>
    /// Tests the full ingestion pipeline via real Node.js sidecar against the simple-project fixture.
    /// Validates: snapshot structure, edge integrity, doc comment preservation, and enum deserialization.
    /// </summary>
    [Fact(Skip = "Requires Node.js and compiled sidecar. Set RUN_SIDECAR_TESTS=true and run: dotnet test --filter 'FullyQualifiedName~TypeScriptSidecarIntegration'")]
    public async Task RealSidecar_SimpleProject_Produces_Valid_Snapshot()
    {
        var tsconfigPath = FindSimpleProjectTsconfig();
        tsconfigPath.Should().NotBeNull("simple-project fixture tsconfig.json must be locatable from test assembly directory");

        var service = CreateIngestionService(_tempDir);

        // Act: full pipeline — real Node.js sidecar → JSON → C# deserialization
        var result = await service.IngestTypeScriptAsync(tsconfigPath!, CancellationToken.None);

        // Verify ingestion result
        result.Should().NotBeNull();
        result.SnapshotId.Should().NotBeNullOrEmpty("ingestion must produce a non-empty snapshot ID");
        result.SymbolCount.Should().BeGreaterThan(0, "snapshot must contain symbols from the fixture");
        result.Skipped.Should().BeFalse("fresh ingestion should not be skipped");

        // Load snapshot for further assertions
        var store = new SnapshotStore(_tempDir);
        var snapshot = await store.LoadAsync(result.SnapshotId, CancellationToken.None);
        snapshot.Should().NotBeNull("saved snapshot must be loadable from store");

        // Structural assertions
        snapshot!.Nodes.Should().NotBeEmpty("snapshot must contain nodes");
        snapshot.Edges.Should().NotBeEmpty("snapshot must contain edges");

        // Edge integrity: all edge endpoints must have non-empty SymbolId values
        foreach (var edge in snapshot.Edges)
        {
            edge.From.Value.Should().NotBeNullOrEmpty("edge From must be non-empty SymbolId");
            edge.To.Value.Should().NotBeNullOrEmpty("edge To must be non-empty SymbolId");
        }

        // Must contain inheritance/implementation edges (fixture has SpecialGreeter extends Greeter,
        // Greeter implements IGreeter, ConfiguredGreeter extends Greeter)
        var hasInheritanceOrImpl = snapshot.Edges.Any(e =>
            e.Kind == SymbolEdgeKind.Inherits || e.Kind == SymbolEdgeKind.Implements);
        hasInheritanceOrImpl.Should().BeTrue("fixture has class inheritance and interface implementation");

        // At least one node must have documentation
        snapshot.Nodes.Should().Contain(n => n.Docs != null,
            "fixture symbols have JSDoc comments");

        // All SymbolKind values must be valid enum members (not numeric fallback)
        var validKinds = Enum.GetValues<SymbolKind>().ToHashSet();
        foreach (var node in snapshot.Nodes)
        {
            validKinds.Should().Contain(node.Kind,
                $"node {node.Id.Value} must have a valid SymbolKind (not numeric fallback)");
        }
    }

    /// <summary>
    /// Tests that a snapshot produced by the real sidecar is queryable via the three primary
    /// MCP tools: search_symbols, get_symbol, get_references.
    /// Validates MCPI-01 (real ingestion) and MCPI-02 (query tool compatibility).
    /// </summary>
    [Fact(Skip = "Requires Node.js and compiled sidecar. Set RUN_SIDECAR_TESTS=true and run: dotnet test --filter 'FullyQualifiedName~TypeScriptSidecarIntegration'")]
    public async Task RealSidecar_Snapshot_Is_Queryable()
    {
        var tsconfigPath = FindSimpleProjectTsconfig();
        tsconfigPath.Should().NotBeNull("simple-project fixture tsconfig.json must be locatable");

        // Ingest via real sidecar
        var store = new SnapshotStore(_tempDir);
        var index = new BM25SearchIndex(new LuceneRAMDirectory());
        var options = new DocAgentServerOptions { ArtifactsDir = _tempDir, AllowedPaths = ["**"] };
        var optionsWrapper = Options.Create(options);
        var allowlist = new PathAllowlist(optionsWrapper);
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<TypeScriptIngestionService>();
        var service = new TypeScriptIngestionService(store, index, allowlist, optionsWrapper, logger);

        var result = await service.IngestTypeScriptAsync(tsconfigPath!, CancellationToken.None);
        result.Should().NotBeNull();
        result.SnapshotId.Should().NotBeNullOrEmpty();

        // Wire up query service and tools
        var queryService = new KnowledgeQueryService(index, store);
        var docTools = new DocTools(
            queryService,
            allowlist,
            NullLogger<DocTools>.Instance,
            optionsWrapper,
            store);

        // ── search_symbols validation (MCPI-02) ──────────────────────
        // Search for "Greeter" — fixture has Greeter, IGreeter, SpecialGreeter, ConfiguredGreeter
        var searchJson = await docTools.SearchSymbols("Greeter");
        var searchRoot = JsonDocument.Parse(searchJson).RootElement;
        searchRoot.TryGetProperty("error", out _).Should().BeFalse("search_symbols must not return an error");
        searchRoot.TryGetProperty("results", out var searchResults).Should().BeTrue("search_symbols must return results array");
        searchResults.EnumerateArray().Should().NotBeEmpty("search for 'Greeter' must return at least one result");

        // Grab a symbol ID from search results for get_symbol
        var firstResult = searchResults.EnumerateArray().First();
        firstResult.TryGetProperty("id", out var symbolIdProp).Should().BeTrue("search result must have an id property");
        var symbolId = symbolIdProp.GetString()!;
        symbolId.Should().NotBeNullOrEmpty("search result id must be non-empty");

        // ── get_symbol validation (MCPI-02) ──────────────────────────
        var getSymbolJson = await docTools.GetSymbol(symbolId);
        var getSymbolRoot = JsonDocument.Parse(getSymbolJson).RootElement;
        getSymbolRoot.TryGetProperty("error", out _).Should().BeFalse("get_symbol must not return an error");
        getSymbolRoot.TryGetProperty("symbol", out var symbolNode).Should().BeTrue("get_symbol must return a symbol object");
        symbolNode.TryGetProperty("displayName", out var displayNameProp).Should().BeTrue("symbol must have displayName");
        displayNameProp.GetString().Should().NotBeNullOrEmpty("symbol displayName must be non-empty");

        // ── get_references validation (MCPI-02) ──────────────────────
        // Use IGreeter (which Greeter implements) — it should have reference edges pointing to it
        var iGreeterId = "T:simple-project:src/index.ts:IGreeter";
        var refsJson = await docTools.GetReferences(iGreeterId);
        var refsRoot = JsonDocument.Parse(refsJson).RootElement;
        refsRoot.TryGetProperty("error", out _).Should().BeFalse("get_references must not return an error for a known symbol");
        // References may be empty if the symbol isn't the target of any reference edges in the index,
        // but the call must succeed without error
        refsRoot.TryGetProperty("references", out _).Should().BeTrue("get_references must return a references array");
    }
}
