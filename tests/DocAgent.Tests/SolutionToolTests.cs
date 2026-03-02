using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for SolutionTools.ExplainSolution MCP tool.
/// Covers per-project stats, dependency DAG, doc coverage, stub counts, single-project detection,
/// PathAllowlist denial, and missing snapshot handling.
/// </summary>
public sealed class SolutionToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SolutionToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SolutionToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ─────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────

    private SolutionTools CreateTools(PathAllowlist? allowlist = null)
    {
        var opts = new DocAgentServerOptions { VerboseErrors = true };
        allowlist ??= new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }));
        return new SolutionTools(_store, allowlist, NullLogger<SolutionTools>.Instance, Options.Create(opts));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static SymbolNode BuildNode(
        string id,
        string project,
        SymbolKind kind = SymbolKind.Method,
        Accessibility access = Accessibility.Public,
        NodeKind nodeKind = NodeKind.Real,
        DocComment? docs = null)
        => new SymbolNode(
            Id: new SymbolId(id),
            Kind: kind,
            DisplayName: id.Split('.').Last(),
            FullyQualifiedName: id,
            PreviousIds: [],
            Accessibility: access,
            Docs: docs,
            Span: null,
            ReturnType: null,
            Parameters: [],
            GenericConstraints: [],
            ProjectOrigin: project,
            NodeKind: nodeKind);

    private static SymbolEdge BuildEdge(string from, string to, EdgeScope scope = EdgeScope.IntraProject)
        => new SymbolEdge(new SymbolId(from), new SymbolId(to), SymbolEdgeKind.Calls, scope);

    private static DocComment BuildDoc(string summary)
        => new DocComment(
            Summary: summary,
            Remarks: null,
            Params: new Dictionary<string, string>(),
            TypeParams: new Dictionary<string, string>(),
            Returns: null,
            Examples: [],
            Exceptions: [],
            SeeAlso: []);

    private static SymbolGraphSnapshot BuildSolutionSnapshot(
        string solutionName,
        SymbolNode[] nodes,
        SymbolEdge[] edges)
        => new SymbolGraphSnapshot(
            SchemaVersion: "1.2",
            ProjectName: solutionName,
            SourceFingerprint: Guid.NewGuid().ToString("N"),
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes.ToList(),
            Edges: edges.ToList(),
            SolutionName: solutionName);

    // ─────────────────────────────────────────────────────────────────
    // Test 1: returns project list with node and edge counts
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_ReturnsProjectListWithCounts()
    {
        // Arrange: 2 projects with 3 nodes each, plus some intraProject edges
        var nodes = new[]
        {
            BuildNode("ProjectA.TypeA1", "ProjectA", SymbolKind.Type),
            BuildNode("ProjectA.TypeA2", "ProjectA", SymbolKind.Method),
            BuildNode("ProjectA.TypeA3", "ProjectA", SymbolKind.Property),
            BuildNode("ProjectB.TypeB1", "ProjectB", SymbolKind.Type),
            BuildNode("ProjectB.TypeB2", "ProjectB", SymbolKind.Method),
            BuildNode("ProjectB.TypeB3", "ProjectB", SymbolKind.Property),
        };
        var edges = new[]
        {
            BuildEdge("ProjectA.TypeA1", "ProjectA.TypeA2", EdgeScope.IntraProject),
            BuildEdge("ProjectB.TypeB1", "ProjectB.TypeB2", EdgeScope.IntraProject),
        };

        var snapshot = BuildSolutionSnapshot("MySolution", nodes, edges);
        var saved = await _store.SaveAsync(snapshot);
        var tools = CreateTools();

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert
        root.TryGetProperty("error", out _).Should().BeFalse("should not be an error response");
        root.GetProperty("solutionName").GetString().Should().Be("MySolution");

        var projects = root.GetProperty("projects");
        projects.GetArrayLength().Should().Be(2);

        var projA = projects.EnumerateArray().First(p => p.GetProperty("name").GetString() == "ProjectA");
        projA.GetProperty("nodeCount").GetInt32().Should().Be(3);
        projA.GetProperty("edgeCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var projB = projects.EnumerateArray().First(p => p.GetProperty("name").GetString() == "ProjectB");
        projB.GetProperty("nodeCount").GetInt32().Should().Be(3);
        projB.GetProperty("edgeCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 2: dependency DAG from CrossProject edges
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_ReturnsDagFromCrossProjectEdges()
    {
        // Arrange: ProjectA depends on ProjectB via cross-project edge
        var nodes = new[]
        {
            BuildNode("ProjectA.TypeA", "ProjectA", SymbolKind.Type),
            BuildNode("ProjectB.TypeB", "ProjectB", SymbolKind.Type),
        };
        var edges = new[]
        {
            BuildEdge("ProjectA.TypeA", "ProjectB.TypeB", EdgeScope.CrossProject),
        };

        var snapshot = BuildSolutionSnapshot("TestSolution", nodes, edges);
        var saved = await _store.SaveAsync(snapshot);
        var tools = CreateTools();

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert
        root.TryGetProperty("error", out _).Should().BeFalse();

        var dag = root.GetProperty("dependencyDag");
        dag.TryGetProperty("ProjectA", out var projectADeps).Should().BeTrue("ProjectA should appear in DAG");
        var deps = projectADeps.EnumerateArray().Select(e => e.GetString()).ToList();
        deps.Should().Contain("ProjectB");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 3: doc coverage percentage
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_ReturnsDocCoveragePercentage()
    {
        // Arrange: ProjectA has 2 public methods: 1 documented, 1 not
        var nodes = new[]
        {
            BuildNode("ProjectA.Method1", "ProjectA", SymbolKind.Method, Accessibility.Public,
                docs: BuildDoc("Documented method")),
            BuildNode("ProjectA.Method2", "ProjectA", SymbolKind.Method, Accessibility.Public,
                docs: null),
        };

        var snapshot = BuildSolutionSnapshot("DocSolution", nodes, []);
        var saved = await _store.SaveAsync(snapshot);
        var tools = CreateTools();

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert: 1/2 documented = 50%
        root.TryGetProperty("error", out _).Should().BeFalse();
        var projects = root.GetProperty("projects");
        var proj = projects.EnumerateArray().First(p => p.GetProperty("name").GetString() == "ProjectA");
        proj.GetProperty("docCoveragePercent").GetDouble().Should().BeApproximately(50.0, 0.01);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 4: total stub node count
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_ReturnsTotalStubCount()
    {
        // Arrange: 2 real nodes + 3 stub nodes
        var nodes = new[]
        {
            BuildNode("ProjectA.RealType1", "ProjectA", SymbolKind.Type, nodeKind: NodeKind.Real),
            BuildNode("ProjectA.RealType2", "ProjectA", SymbolKind.Method, nodeKind: NodeKind.Real),
            BuildNode("ExternalLib.StubType1", "ExternalLib", SymbolKind.Type, nodeKind: NodeKind.Stub),
            BuildNode("ExternalLib.StubType2", "ExternalLib", SymbolKind.Method, nodeKind: NodeKind.Stub),
            BuildNode("ExternalLib.StubType3", "ExternalLib", SymbolKind.Property, nodeKind: NodeKind.Stub),
        };

        var snapshot = BuildSolutionSnapshot("StubSolution", nodes, []);
        var saved = await _store.SaveAsync(snapshot);
        var tools = CreateTools();

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.GetProperty("totalStubNodeCount").GetInt32().Should().Be(3);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 5: single-project snapshot sets isSingleProject=true with empty DAG
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_SingleProject_ReturnsIsSingleProjectTrue()
    {
        // Arrange: all nodes from same ProjectOrigin
        var nodes = new[]
        {
            BuildNode("MyProject.TypeA", "MyProject", SymbolKind.Type),
            BuildNode("MyProject.TypeB", "MyProject", SymbolKind.Method),
        };
        var edges = new[]
        {
            BuildEdge("MyProject.TypeA", "MyProject.TypeB", EdgeScope.IntraProject),
        };

        var snapshot = BuildSolutionSnapshot("SingleProjectSolution", nodes, edges);
        var saved = await _store.SaveAsync(snapshot);
        var tools = CreateTools();

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert
        root.TryGetProperty("error", out _).Should().BeFalse();
        root.GetProperty("isSingleProject").GetBoolean().Should().BeTrue();

        var dag = root.GetProperty("dependencyDag");
        dag.EnumerateObject().Should().BeEmpty("DAG should be empty for single-project solutions");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 6: PathAllowlist denied returns opaque denial
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_PathAllowlistDenied_ReturnsOpaqueDenial()
    {
        // Arrange: save a snapshot first so the hash is valid
        var nodes = new[] { BuildNode("ProjectA.Type", "ProjectA", SymbolKind.Type) };
        var snapshot = BuildSolutionSnapshot("DeniedSolution", nodes, []);
        var saved = await _store.SaveAsync(snapshot);

        // Use an allowlist that denies the temp directory (no AllowedPaths configured)
        var allowlist = new PathAllowlist(Options.Create(new DocAgentServerOptions()));
        var tools = CreateTools(allowlist);

        // Act
        var json = await tools.ExplainSolution(saved.ContentHash!);
        var root = Parse(json);

        // Assert: should return opaque error without revealing allowlist details
        root.TryGetProperty("error", out var errProp).Should().BeTrue();
        errProp.GetString().Should().Be("not_found");

        root.GetProperty("message").GetString().Should().Be("Solution not found.");
        // Must NOT contain allowlist details
        json.Should().NotContain("allowlist");
        json.Should().NotContain("AllowedPaths");
        json.Should().NotContain(_tempDir);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 7: missing snapshot returns Solution not found
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainSolution_MissingSnapshot_ReturnsNotFound()
    {
        var tools = CreateTools();

        // Act: call with a hash that doesn't exist
        var json = await tools.ExplainSolution("non-existent-hash-000000");
        var root = Parse(json);

        // Assert
        root.TryGetProperty("error", out var errProp).Should().BeTrue();
        errProp.GetString().Should().Be("not_found");
        root.GetProperty("message").GetString().Should().Contain("Solution not found");
    }
}
