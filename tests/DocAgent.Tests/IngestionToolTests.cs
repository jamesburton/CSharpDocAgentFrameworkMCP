using System.Text.Json;
using DocAgent.Core;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Unit tests for <see cref="IngestionTools"/> using a stub <see cref="IIngestionService"/>.
/// McpServer and RequestContext are passed as null — progress notification is never triggered in unit tests.
/// </summary>
public sealed class IngestionToolTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly IngestionResult DefaultResult = new(
        SnapshotId: "abc123",
        SymbolCount: 42,
        ProjectCount: 3,
        Duration: TimeSpan.FromSeconds(2.5),
        Warnings: new[] { "W1", "W2" });

    /// <summary>
    /// Invoke the tool with null MCP-framework args (safe when progressToken is null).
    /// </summary>
    private static Task<string> CallTool(
        IngestionTools tools,
        string path,
        string? include = null,
        string? exclude = null,
        bool forceReindex = false,
        CancellationToken ct = default)
    {
        // Passing null! for McpServer and RequestContext is safe in tests because:
        //   - RequestContext?.Params?.Meta is null, so progressToken is null.
        //   - progressCallback is therefore null and McpServer.SendNotificationAsync is never called.
        return tools.IngestProject(
            null!,
            null!,
            path,
            include,
            exclude,
            forceReindex,
            ct);
    }

    private static IngestionTools CreateTools(
        IIngestionService? svc = null,
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
            svc ?? new StubIngestionService(DefaultResult),
            new StubSolutionIngestionService(),
            allowlist,
            NullLogger<IngestionTools>.Instance);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubIngestionService : IIngestionService
    {
        private readonly IngestionResult _result;

        public string? LastPath { get; private set; }
        public string? LastInclude { get; private set; }
        public string? LastExclude { get; private set; }
        public bool LastForceReindex { get; private set; }

        public StubIngestionService(IngestionResult result) => _result = result;

        public Task<IngestionResult> IngestAsync(
            string path,
            string? includeGlob,
            string? excludeGlob,
            bool forceReindex,
            Func<int, int, string, Task>? reportProgress,
            CancellationToken cancellationToken,
            bool forceFullReingestion = false)
        {
            LastPath = path;
            LastInclude = includeGlob;
            LastExclude = excludeGlob;
            LastForceReindex = forceReindex;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubSolutionIngestionService : ISolutionIngestionService
    {
        public Task<SolutionIngestionResult> IngestAsync(
            string slnPath,
            Func<int, int, string, Task>? reportProgress,
            CancellationToken cancellationToken,
            bool forceFullReingest = false)
            => Task.FromResult(new SolutionIngestionResult(
                "stub", "stub", 0, 0, 0, 0, TimeSpan.Zero, [], []));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestProject_NullPath_ReturnsErrorJson()
    {
        var tools = CreateTools();
        var json = await CallTool(tools, null!);
        var root = Parse(json);

        root.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetString().Should().Be("invalid_input");
    }

    [Fact]
    public async Task IngestProject_EmptyPath_ReturnsErrorJson()
    {
        var tools = CreateTools();
        var json = await CallTool(tools, "   ");
        var root = Parse(json);

        root.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetString().Should().Be("invalid_input");
    }

    [Fact]
    public async Task IngestProject_PathOutsideAllowlist_ReturnsAccessDenied()
    {
        var tools = CreateTools(permissiveAllowlist: false);
        var json = await CallTool(tools, "/some/path/project.sln");
        var root = Parse(json);

        root.TryGetProperty("error", out var err).Should().BeTrue();
        err.GetString().Should().Be("access_denied");
    }

    [Fact]
    public async Task IngestProject_ValidPath_CallsServiceWithNormalizedPath()
    {
        var stub = new StubIngestionService(DefaultResult);
        var tools = CreateTools(svc: stub);
        var path = "C:/projects/foo.csproj";

        await CallTool(tools, path);

        stub.LastPath.Should().NotBeNullOrWhiteSpace();
        stub.LastPath.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task IngestProject_Success_ResponseHasRequiredFields()
    {
        var tools = CreateTools();
        var json = await CallTool(tools, "C:/foo.sln");
        var root = Parse(json);

        root.TryGetProperty("snapshotId", out var snapId).Should().BeTrue();
        snapId.GetString().Should().Be("abc123");

        root.TryGetProperty("symbolCount", out var sc).Should().BeTrue();
        sc.GetInt32().Should().Be(42);

        root.TryGetProperty("projectCount", out var pc).Should().BeTrue();
        pc.GetInt32().Should().Be(3);

        root.TryGetProperty("durationMs", out var dur).Should().BeTrue();
        dur.GetDouble().Should().BeApproximately(2500.0, 1.0);

        root.TryGetProperty("warnings", out var w).Should().BeTrue();
        w.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task IngestProject_IncludeExclude_ForwardedToService()
    {
        var stub = new StubIngestionService(DefaultResult);
        var tools = CreateTools(svc: stub);

        await CallTool(tools, "C:/foo.sln",
            include: "**/App/**", exclude: "**/Tests/**", forceReindex: true);

        stub.LastInclude.Should().Be("**/App/**");
        stub.LastExclude.Should().Be("**/Tests/**");
        stub.LastForceReindex.Should().BeTrue();
    }

    [Fact]
    public async Task IngestProject_IndexError_IncludedInResponse()
    {
        var result = DefaultResult with { IndexError = "Lucene write failed" };
        var tools = CreateTools(svc: new StubIngestionService(result));
        var json = await CallTool(tools, "C:/foo.sln");
        var root = Parse(json);

        root.TryGetProperty("indexError", out var indexErr).Should().BeTrue();
        indexErr.GetString().Should().Be("Lucene write failed");
    }
}
