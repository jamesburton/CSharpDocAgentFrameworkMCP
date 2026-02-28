using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer;
using DocAgent.McpServer.Config;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DocAgent.Tests;

/// <summary>
/// End-to-end integration tests proving the full pipeline works through real DI container.
/// Uses a synthetic SymbolGraphSnapshot to drive: store -> index -> query.
/// </summary>
[Trait("Category", "Integration")]
public class E2EIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public E2EIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "E2EIntegrationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ---------- helpers -------------------------------------------------------

    private static DocComment MakeDoc(string summary) =>
        new(
            Summary: summary,
            Remarks: null,
            Params: new Dictionary<string, string>(),
            TypeParams: new Dictionary<string, string>(),
            Returns: null,
            Examples: [],
            Exceptions: [],
            SeeAlso: []);

    private static SymbolNode MakeNode(string id, SymbolKind kind, string displayName, string fqn, string summary) =>
        new(
            Id: new SymbolId(id),
            Kind: kind,
            DisplayName: displayName,
            FullyQualifiedName: fqn,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: MakeDoc(summary),
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>());

    private static SymbolGraphSnapshot BuildSyntheticSnapshot()
    {
        var nsNode = MakeNode("N:TestProject", SymbolKind.Namespace, "TestProject", "TestProject",
            "Root namespace for the test project");
        var typeNode = MakeNode("T:TestProject.Calculator", SymbolKind.Type, "Calculator", "TestProject.Calculator",
            "A simple calculator type used for arithmetic operations");
        var methodNode = MakeNode("M:TestProject.Calculator.Add", SymbolKind.Method, "Add", "TestProject.Calculator.Add",
            "Adds two numbers together and returns the sum");
        var propNode = MakeNode("P:TestProject.Calculator.LastResult", SymbolKind.Property, "LastResult",
            "TestProject.Calculator.LastResult", "The last result computed by the calculator");

        var edges = new SymbolEdge[]
        {
            // Contains hierarchy
            new(new SymbolId("N:TestProject"), new SymbolId("T:TestProject.Calculator"), SymbolEdgeKind.Contains),
            new(new SymbolId("T:TestProject.Calculator"), new SymbolId("M:TestProject.Calculator.Add"), SymbolEdgeKind.Contains),
            new(new SymbolId("T:TestProject.Calculator"), new SymbolId("P:TestProject.Calculator.LastResult"), SymbolEdgeKind.Contains),
            // Non-Contains edge for GetReferences test: Add method references LastResult property
            new(new SymbolId("M:TestProject.Calculator.Add"), new SymbolId("P:TestProject.Calculator.LastResult"), SymbolEdgeKind.References),
        };

        return new SymbolGraphSnapshot(
            SchemaVersion: "1.0",
            ProjectName: "TestProject",
            SourceFingerprint: "e2e-test-fp",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: [nsNode, typeNode, methodNode, propNode],
            Edges: edges);
    }

    private ServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.Configure<DocAgentServerOptions>(o => o.ArtifactsDir = _tempDir);
        services.AddDocAgent();
        return services;
    }

    // ---------- tests ---------------------------------------------------------

    [Fact]
    public async Task DI_Container_Resolves_All_Services()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        store.Should().NotBeNull();
        index.Should().NotBeNull();
        query.Should().NotBeNull();
    }

    [Fact]
    public async Task Full_Pipeline_SearchAsync_Returns_Results()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();

        var snapshot = BuildSyntheticSnapshot();
        var saved = await store.SaveAsync(snapshot);
        await index.IndexAsync(saved, CancellationToken.None);

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        var result = await query.SearchAsync("Calculator");

        result.Success.Should().BeTrue();
        result.Value!.Payload.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Full_Pipeline_GetSymbolAsync_Returns_Detail()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();

        var snapshot = BuildSyntheticSnapshot();
        var saved = await store.SaveAsync(snapshot);
        await index.IndexAsync(saved, CancellationToken.None);

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        var calculatorId = new SymbolId("T:TestProject.Calculator");
        var result = await query.GetSymbolAsync(calculatorId);

        result.Success.Should().BeTrue();
        result.Value!.Payload.Node.DisplayName.Should().Be("Calculator");
    }

    [Fact]
    public async Task Full_Pipeline_GetReferencesAsync_Returns_Edges()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();

        var snapshot = BuildSyntheticSnapshot();
        var saved = await store.SaveAsync(snapshot);
        await index.IndexAsync(saved, CancellationToken.None);

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        // Calculator has Contains edges to Add and LastResult, so edges should be non-empty
        var calculatorId = new SymbolId("T:TestProject.Calculator");
        var edges = new List<SymbolEdge>();
        await foreach (var edge in query.GetReferencesAsync(calculatorId))
            edges.Add(edge);

        edges.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Full_Pipeline_DiffAsync_Returns_Diff()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();

        // Save snapshot A
        var snapshotA = BuildSyntheticSnapshot();
        var savedA = await store.SaveAsync(snapshotA);
        await index.IndexAsync(savedA, CancellationToken.None);

        // Save snapshot B — add an extra node
        var extraNode = MakeNode("T:TestProject.Adder", SymbolKind.Type, "Adder", "TestProject.Adder",
            "An extra adder type added in version B");
        var nodesB = snapshotA.Nodes.Concat([extraNode]).ToList();
        var snapshotB = snapshotA with
        {
            ContentHash = null,
            Nodes = nodesB,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            SourceFingerprint = "e2e-test-fp-b"
        };
        var savedB = await store.SaveAsync(snapshotB);
        await index.IndexAsync(savedB, CancellationToken.None);

        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IKnowledgeQueryService>();

        var result = await query.DiffAsync(
            new SnapshotRef(savedA.ContentHash!),
            new SnapshotRef(savedB.ContentHash!));

        result.Success.Should().BeTrue();
        result.Value!.Payload.Entries.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ArtifactsDir_Flows_To_Both_Services()
    {
        var services = BuildServices();
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<SnapshotStore>();
        var index = provider.GetRequiredService<ISearchIndex>();

        var snapshot = BuildSyntheticSnapshot();
        var saved = await store.SaveAsync(snapshot);
        await index.IndexAsync(saved, CancellationToken.None);

        // Both SnapshotStore and BM25SearchIndex should have written files to the configured temp dir
        Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories).Should().NotBeEmpty();
    }
}
