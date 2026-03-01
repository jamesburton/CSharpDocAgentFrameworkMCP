using System.Text.Json;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Lucene.Net.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Tests for get_symbol FQN disambiguation across multiple projects.
/// Phase 15 — TOOLS-02.
/// </summary>
[Trait("Category", "Unit")]
public class GetSymbolFqnDisambiguationTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ---------- helpers -------------------------------------------------------

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FqnDisambig_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static SymbolNode MakeNode(string id, string fqn, string? project = null) =>
        new(
            Id: new SymbolId(id),
            Kind: SymbolKind.Type,
            DisplayName: fqn.Split('.').Last(),
            FullyQualifiedName: fqn,
            PreviousIds: [],
            Accessibility: Accessibility.Public,
            Docs: null,
            Span: null,
            ReturnType: null,
            Parameters: Array.Empty<ParameterInfo>(),
            GenericConstraints: Array.Empty<GenericConstraint>(),
            ProjectOrigin: project,
            NodeKind: NodeKind.Real);

    private static SymbolGraphSnapshot BuildSnapshot(SymbolNode[] nodes) =>
        new(
            SchemaVersion: "v1",
            ProjectName: "TestProject",
            SourceFingerprint: "fp1",
            ContentHash: null,
            CreatedAt: DateTimeOffset.UtcNow,
            Nodes: nodes,
            Edges: []);

    private async Task<DocTools> BuildTools(SymbolNode[] nodes)
    {
        var snapshot = BuildSnapshot(nodes);
        var dir = CreateTempDir();
        var store = new SnapshotStore(dir);
        var saved = await store.SaveAsync(snapshot);

        var index = new BM25SearchIndex(new RAMDirectory());
        await index.IndexAsync(saved, CancellationToken.None);

        var svc = new KnowledgeQueryService(index, store);
        var opts = new DocAgentServerOptions { VerboseErrors = true, AllowedPaths = ["**"] };
        var allowlist = new PathAllowlist(Options.Create(opts));
        return new DocTools(svc, allowlist, NullLogger<DocTools>.Instance, Options.Create(opts));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------- tests ---------------------------------------------------------

    [Fact]
    public async Task GetSymbol_WithStableSymbolId_ResolvesDirectly()
    {
        // A stable SymbolId contains '|' — FQN scan is skipped entirely.
        // We use the existing stub-based approach: the service returns NotFound for unknown ids,
        // so the key thing is it doesn't try FQN resolution and returns "not_found" JSON.
        var node = MakeNode("MyAssembly|MyNamespace.MyClass", "MyNamespace.MyClass", "ProjectA");
        var tools = await BuildTools([node]);

        // Use the exact id with '|' — should resolve directly
        var json = await tools.GetSymbol("MyAssembly|MyNamespace.MyClass");
        var doc = JsonDocument.Parse(json);

        // Should succeed (find via SymbolId lookup)
        doc.RootElement.TryGetProperty("error", out _).Should().BeFalse("stable SymbolId should resolve without FQN scan");
        doc.RootElement.GetProperty("id").GetString().Should().Be("MyAssembly|MyNamespace.MyClass");
    }

    [Fact]
    public async Task GetSymbol_WithUniqueFqn_ResolvesSuccessfully()
    {
        // FQN exists in only one project — should resolve to that symbol.
        var node = MakeNode("ProjectA|MyNamespace.UniqueClass", "MyNamespace.UniqueClass", "ProjectA");
        var tools = await BuildTools([node]);

        var json = await tools.GetSymbol("MyNamespace.UniqueClass");
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("error", out _).Should().BeFalse("unique FQN should resolve successfully");
        doc.RootElement.GetProperty("fullyQualifiedName").GetString().Should().Be("MyNamespace.UniqueClass");
    }

    [Fact]
    public async Task GetSymbol_WithAmbiguousFqn_ReturnsErrorListingProjects()
    {
        // Same FQN in two different projects — disambiguation error expected.
        var nodeA = MakeNode("ProjectA|MyNamespace.SharedClass", "MyNamespace.SharedClass", "ProjectA");
        var nodeB = MakeNode("ProjectB|MyNamespace.SharedClass", "MyNamespace.SharedClass", "ProjectB");
        var tools = await BuildTools([nodeA, nodeB]);

        var json = await tools.GetSymbol("MyNamespace.SharedClass");
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("error", out var errorProp).Should().BeTrue("ambiguous FQN should return error");
        errorProp.GetString().Should().Be("invalid_input");

        var message = doc.RootElement.GetProperty("message").GetString()!;
        message.Should().Contain("Ambiguous FQN");
        message.Should().Contain("ProjectA");
        message.Should().Contain("ProjectB");
    }

    [Fact]
    public async Task GetSymbol_WithUnknownFqn_ReturnsNotFound()
    {
        // FQN that doesn't exist — should return not_found.
        var node = MakeNode("ProjectA|MyNamespace.RealClass", "MyNamespace.RealClass", "ProjectA");
        var tools = await BuildTools([node]);

        var json = await tools.GetSymbol("MyNamespace.DoesNotExist");
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("error", out var errorProp).Should().BeTrue("unknown FQN should return error");
        errorProp.GetString().Should().Be("not_found");
    }
}
