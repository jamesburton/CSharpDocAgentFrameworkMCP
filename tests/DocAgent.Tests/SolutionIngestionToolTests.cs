using System.Text.Json;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for <see cref="IngestionTools.IngestSolution"/> using stub implementations.
/// McpServer and RequestContext are passed as null — progress notification is never triggered in unit tests.
/// </summary>
public sealed class SolutionIngestionToolTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly SolutionIngestionResult DefaultResult = new(
        SnapshotId: "sol-abc123",
        SolutionName: "MyAwesomeSolution",
        TotalProjectCount: 5,
        IngestedProjectCount: 4,
        TotalNodeCount: 320,
        TotalEdgeCount: 88,
        Duration: TimeSpan.FromSeconds(3.7),
        Projects: new[]
        {
            new ProjectIngestionStatus("App", "/src/App/App.csproj", "ok", null, 200, "net10.0"),
            new ProjectIngestionStatus("Lib", "/src/Lib/Lib.csproj", "ok", null, 120, "net10.0"),
            new ProjectIngestionStatus("Legacy", "/src/Legacy/Legacy.csproj", "skipped", "Non-C# project", null, null),
            new ProjectIngestionStatus("Tests", "/src/Tests/Tests.csproj", "ok", null, 0, "net10.0"),
            new ProjectIngestionStatus("Broken", "/src/Broken/Broken.csproj", "failed", "MSBuild load failed", null, null),
        },
        Warnings: new[] { "Workspace diagnostic: foo" });

    /// <summary>
    /// Invoke the IngestSolution tool with null MCP-framework args (safe when progressToken is null).
    /// </summary>
    private static Task<string> CallTool(
        IngestionTools tools,
        string path,
        CancellationToken ct = default)
    {
        // Passing null! for McpServer and RequestContext is safe in tests because:
        //   - RequestContext?.Params?.Meta is null, so progressToken is null.
        //   - progressCallback is therefore null and McpServer.SendNotificationAsync is never called.
        return tools.IngestSolution(
            null!,
            null!,
            path,
            ct);
    }

    private static IngestionTools CreateTools(
        ISolutionIngestionService? solutionSvc = null,
        bool permissiveAllowlist = true)
    {
        PathAllowlist allowlist;
        if (permissiveAllowlist)
        {
            var opts = new DocAgentServerOptions { AllowedPaths = ["**"] };
            allowlist = new PathAllowlist(Options.Create(opts));
        }
        else
        {
            allowlist = new PathAllowlist(Options.Create(new DocAgentServerOptions()));
        }

        return new IngestionTools(
            new StubIngestionService(),
            solutionSvc ?? new StubSolutionIngestionService(DefaultResult),
            allowlist,
            NullLogger<IngestionTools>.Instance);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubIngestionService : IIngestionService
    {
        public Task<IngestionResult> IngestAsync(
            string path,
            string? includeGlob,
            string? excludeGlob,
            bool forceReindex,
            Func<int, int, string, Task>? reportProgress,
            CancellationToken cancellationToken,
            bool forceFullReingestion = false)
            => Task.FromResult(new IngestionResult("stub", 0, 0, TimeSpan.Zero, []));
    }

    private sealed class StubSolutionIngestionService : ISolutionIngestionService
    {
        private readonly SolutionIngestionResult? _result;
        private readonly Exception? _throws;

        public bool WasCalled { get; private set; }
        public string? LastPath { get; private set; }

        public StubSolutionIngestionService(SolutionIngestionResult result) => _result = result;

        public StubSolutionIngestionService(Exception throws) => _throws = throws;

        public Task<SolutionIngestionResult> IngestAsync(
            string slnPath,
            Func<int, int, string, Task>? reportProgress,
            CancellationToken cancellationToken,
            bool forceFullReingest = false)
        {
            WasCalled = true;
            LastPath = slnPath;
            if (_throws is not null) throw _throws;
            return Task.FromResult(_result!);
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestSolution_PathOutsideAllowlist_ReturnsAccessDenied_AndDoesNotCallService()
    {
        var stub = new StubSolutionIngestionService(DefaultResult);
        var tools = CreateTools(solutionSvc: stub, permissiveAllowlist: false);

        var json = await CallTool(tools, "/some/secret/path/MySolution.sln");
        var root = Parse(json);

        root.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetString().Should().Be("access_denied");
        stub.WasCalled.Should().BeFalse("allowlist must gate before service delegation");
    }

    [Fact]
    public async Task IngestSolution_PathInsideAllowlist_CallsServiceWithNormalizedPath()
    {
        var stub = new StubSolutionIngestionService(DefaultResult);
        var tools = CreateTools(solutionSvc: stub);
        var path = "C:/projects/MySolution.sln";

        await CallTool(tools, path);

        stub.WasCalled.Should().BeTrue();
        stub.LastPath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task IngestSolution_NullOrEmptyPath_ReturnsInvalidInput()
    {
        var tools = CreateTools();

        var jsonNull = await CallTool(tools, null!);
        Parse(jsonNull).GetProperty("error").GetString().Should().Be("invalid_input");

        var jsonEmpty = await CallTool(tools, "   ");
        Parse(jsonEmpty).GetProperty("error").GetString().Should().Be("invalid_input");
    }

    [Fact]
    public async Task IngestSolution_Success_ResponseHasAllRequiredFields()
    {
        var tools = CreateTools();
        var json = await CallTool(tools, "C:/projects/MySolution.sln");
        var root = Parse(json);

        root.TryGetProperty("snapshotId", out var snapId).Should().BeTrue();
        snapId.GetString().Should().Be("sol-abc123");

        root.TryGetProperty("solutionName", out var slnName).Should().BeTrue();
        slnName.GetString().Should().Be("MyAwesomeSolution");

        root.TryGetProperty("totalProjectCount", out var total).Should().BeTrue();
        total.GetInt32().Should().Be(5);

        root.TryGetProperty("ingestedProjectCount", out var ingested).Should().BeTrue();
        ingested.GetInt32().Should().Be(4);

        root.TryGetProperty("totalNodeCount", out var nodes).Should().BeTrue();
        nodes.GetInt32().Should().Be(320);

        root.TryGetProperty("totalEdgeCount", out var edges).Should().BeTrue();
        edges.GetInt32().Should().Be(88);

        root.TryGetProperty("durationMs", out var dur).Should().BeTrue();
        dur.GetDouble().Should().BeApproximately(3700.0, 1.0);

        root.TryGetProperty("projects", out var projects).Should().BeTrue();
        projects.GetArrayLength().Should().Be(5);

        root.TryGetProperty("warnings", out var warnings).Should().BeTrue();
        warnings.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task IngestSolution_ServiceThrows_ReturnsIngestionFailed()
    {
        var stub = new StubSolutionIngestionService(new InvalidOperationException("MSBuild exploded"));
        var tools = CreateTools(solutionSvc: stub);

        var json = await CallTool(tools, "C:/projects/MySolution.sln");
        var root = Parse(json);

        root.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetString().Should().Be("ingestion_failed");
        root.TryGetProperty("message", out var msg).Should().BeTrue();
        msg.GetString().Should().Contain("MSBuild exploded");
    }
}
