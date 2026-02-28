using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using DocAgent.Tests.SemanticDiff;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests.ChangeReview;

/// <summary>
/// Unit tests for all three MCP change tools: review_changes, find_breaking_changes, explain_change.
/// ChangeTools is instantiated directly with a real SnapshotStore pointing to a temp directory.
/// </summary>
public sealed class ChangeToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public ChangeToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ChangeToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ─────────────────────────────────────────────────────────────────
    // Test helper
    // ─────────────────────────────────────────────────────────────────

    private ChangeTools CreateTools(SnapshotStore? store = null)
    {
        store ??= _store;
        var opts = new DocAgentServerOptions { VerboseErrors = true };
        var allowlist = new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }));
        var logger = NullLogger<ChangeTools>.Instance;
        return new ChangeTools(store, allowlist, logger, Options.Create(opts));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private async Task<(string hashA, string hashB)> SaveBreakingPairAsync()
    {
        // Before: method exists with string return
        var nodeA = DiffTestHelpers.BuildMethod("TestProject.TestClass.MyMethod", access: Accessibility.Public, returnType: "string");
        var snapshotA = DiffTestHelpers.BuildSnapshot([nodeA], []);
        var savedA = await _store.SaveAsync(snapshotA);

        // After: method removed (breaking)
        var snapshotB = DiffTestHelpers.BuildSnapshot([], []);
        var savedB = await _store.SaveAsync(snapshotB);

        return (savedA.ContentHash!, savedB.ContentHash!);
    }

    private async Task<(string hashA, string hashB)> SaveDocOnlyPairAsync()
    {
        // Before and after differ only in doc comment
        var nodeA = DiffTestHelpers.BuildMethod("TestProject.TestClass.DocMethod",
            docs: DiffTestHelpers.BuildDoc("Old summary"));
        var snapshotA = DiffTestHelpers.BuildSnapshot([nodeA], []);
        var savedA = await _store.SaveAsync(snapshotA);

        var nodeB = DiffTestHelpers.BuildMethod("TestProject.TestClass.DocMethod",
            docs: DiffTestHelpers.BuildDoc("New summary"));
        var snapshotB = DiffTestHelpers.BuildSnapshot([nodeB], []);
        var savedB = await _store.SaveAsync(snapshotB);

        return (savedA.ContentHash!, savedB.ContentHash!);
    }

    private async Task<(string hashA, string hashB)> SaveModifiedMethodPairAsync()
    {
        // Before: method with int return, After: method with string return
        var nodeA = DiffTestHelpers.BuildMethod("TestProject.TestClass.ChangedMethod", access: Accessibility.Public, returnType: "int");
        var snapshotA = DiffTestHelpers.BuildSnapshot([nodeA], []);
        var savedA = await _store.SaveAsync(snapshotA);

        var nodeB = DiffTestHelpers.BuildMethod("TestProject.TestClass.ChangedMethod", access: Accessibility.Public, returnType: "string");
        var snapshotB = DiffTestHelpers.BuildSnapshot([nodeB], []);
        var savedB = await _store.SaveAsync(snapshotB);

        return (savedA.ContentHash!, savedB.ContentHash!);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 1: review_changes — returns findings grouped by severity
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewChanges_Returns_Findings_Grouped_By_Severity()
    {
        var (hashA, hashB) = await SaveBreakingPairAsync();
        var tools = CreateTools();

        var json = await tools.ReviewChanges(hashA, hashB);
        var root = Parse(json);

        root.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.GetProperty("breaking").GetInt32().Should().BeGreaterThan(0);

        root.TryGetProperty("findings", out var findings).Should().BeTrue();
        findings.GetArrayLength().Should().BeGreaterThan(0);

        // Verify at least one finding has severity "breaking"
        var hasBreahing = false;
        foreach (var f in findings.EnumerateArray())
        {
            if (f.TryGetProperty("severity", out var sev) && sev.GetString() == "breaking")
            {
                hasBreahing = true;
                break;
            }
        }
        hasBreahing.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 2: review_changes — snapshot not found returns structured error
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewChanges_Snapshot_Not_Found_Returns_Error()
    {
        var tools = CreateTools();

        var json = await tools.ReviewChanges("non-existent-hash-abc123", "another-missing-hash");
        var root = Parse(json);

        root.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().Be("snapshot_not_found");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 3: review_changes — mismatched projects returns structured error
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewChanges_Mismatched_Projects_Returns_Error()
    {
        var nodeA = DiffTestHelpers.BuildMethod("ProjectA.Class.Method");
        var snapshotA = DiffTestHelpers.BuildSnapshot("ProjectA", [nodeA], []);
        var savedA = await _store.SaveAsync(snapshotA);

        var nodeB = DiffTestHelpers.BuildMethod("ProjectB.Class.Method");
        var snapshotB = DiffTestHelpers.BuildSnapshot("ProjectB", [nodeB], []);
        var savedB = await _store.SaveAsync(snapshotB);

        var tools = CreateTools();
        var json = await tools.ReviewChanges(savedA.ContentHash!, savedB.ContentHash!);
        var root = Parse(json);

        root.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().Be("invalid_input");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 4: review_changes — verbose includes trivials
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewChanges_Verbose_Includes_Trivials()
    {
        var (hashA, hashB) = await SaveDocOnlyPairAsync();
        var tools = CreateTools();

        // Verbose = true: findings present
        var verboseJson = await tools.ReviewChanges(hashA, hashB, verbose: true);
        var verboseRoot = Parse(verboseJson);
        verboseRoot.GetProperty("findings").GetArrayLength().Should().BeGreaterThan(0);
        verboseRoot.GetProperty("summary").GetProperty("trivialFiltered").GetInt32().Should().Be(0);

        // Verbose = false: findings empty but trivialFiltered > 0
        var quietJson = await tools.ReviewChanges(hashA, hashB, verbose: false);
        var quietRoot = Parse(quietJson);
        quietRoot.GetProperty("findings").GetArrayLength().Should().Be(0);
        quietRoot.GetProperty("summary").GetProperty("trivialFiltered").GetInt32().Should().BeGreaterThan(0);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 5: find_breaking_changes — returns only breaking
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindBreakingChanges_Returns_Only_Breaking()
    {
        // Mixed: one breaking removal + one non-breaking doc change
        var methodNode = DiffTestHelpers.BuildMethod("TestProject.TestClass.PublicMethod", access: Accessibility.Public, returnType: "string");
        var docNode = DiffTestHelpers.BuildMethod("TestProject.TestClass.DocOnlyMethod",
            docs: DiffTestHelpers.BuildDoc("Old summary"));

        var snapshotA = DiffTestHelpers.BuildSnapshot([methodNode, docNode], []);
        var savedA = await _store.SaveAsync(snapshotA);

        // After: method removed (breaking), doc changed (informational)
        var docNodeB = DiffTestHelpers.BuildMethod("TestProject.TestClass.DocOnlyMethod",
            docs: DiffTestHelpers.BuildDoc("New summary"));
        var snapshotB = DiffTestHelpers.BuildSnapshot([docNodeB], []);
        var savedB = await _store.SaveAsync(snapshotB);

        var tools = CreateTools();
        var json = await tools.FindBreakingChanges(savedA.ContentHash!, savedB.ContentHash!);
        var root = Parse(json);

        root.GetProperty("breakingCount").GetInt32().Should().Be(1);
        var breakingChanges = root.GetProperty("breakingChanges");
        breakingChanges.GetArrayLength().Should().Be(1);
        breakingChanges[0].GetProperty("symbolId").GetString().Should().Contain("PublicMethod");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 6: find_breaking_changes — no breaking returns zero
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FindBreakingChanges_No_Breaking_Returns_Zero()
    {
        var (hashA, hashB) = await SaveDocOnlyPairAsync();
        var tools = CreateTools();

        var json = await tools.FindBreakingChanges(hashA, hashB);
        var root = Parse(json);

        root.GetProperty("breakingCount").GetInt32().Should().Be(0);
        root.GetProperty("breakingChanges").GetArrayLength().Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 7: explain_change — returns detailed explanation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainChange_Returns_Detailed_Explanation()
    {
        var (hashA, hashB) = await SaveModifiedMethodPairAsync();
        var tools = CreateTools();

        const string symbolId = "TestProject.TestClass.ChangedMethod";
        var json = await tools.ExplainChange(hashA, hashB, symbolId);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse("should not be an error response");
        root.GetProperty("symbolId").GetString().Should().Be(symbolId);
        root.GetProperty("changeType").GetString().Should().NotBeNullOrEmpty();

        var changes = root.GetProperty("changes");
        changes.GetArrayLength().Should().BeGreaterThan(0);

        var firstChange = changes[0];
        firstChange.TryGetProperty("before", out _).Should().BeTrue();
        firstChange.TryGetProperty("after", out _).Should().BeTrue();
        firstChange.GetProperty("whyItMatters").GetString().Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 8: explain_change — symbol not changed returns error
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExplainChange_Symbol_Not_Changed_Returns_Error()
    {
        var (hashA, hashB) = await SaveBreakingPairAsync();
        var tools = CreateTools();

        // Use a symbolId that was not changed between the two snapshots
        var json = await tools.ExplainChange(hashA, hashB, "TestProject.UnchangedSymbol.NotExist");
        var root = Parse(json);

        root.TryGetProperty("error", out var errorProp).Should().BeTrue();
        errorProp.GetString().Should().Be("not_found");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 9: All tools support markdown format
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task All_Tools_Support_Markdown_Format()
    {
        var (hashA, hashB) = await SaveBreakingPairAsync();
        var tools = CreateTools();

        var reviewMd = await tools.ReviewChanges(hashA, hashB, format: "markdown");
        reviewMd.Should().NotBeNullOrEmpty();
        reviewMd.Should().NotStartWith("{");  // not JSON

        var breakingMd = await tools.FindBreakingChanges(hashA, hashB, format: "markdown");
        breakingMd.Should().NotBeNullOrEmpty();
        breakingMd.Should().NotStartWith("{");

        const string symbolId = "TestProject.TestClass.MyMethod";
        var explainMd = await tools.ExplainChange(hashA, hashB, symbolId, format: "markdown");
        explainMd.Should().NotBeNullOrEmpty();
        explainMd.Should().NotStartWith("{");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 10: All tools support tron format
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task All_Tools_Support_Tron_Format()
    {
        var (hashA, hashB) = await SaveBreakingPairAsync();
        var tools = CreateTools();

        var reviewTron = await tools.ReviewChanges(hashA, hashB, format: "tron");
        reviewTron.Should().Contain("$schema");

        var breakingTron = await tools.FindBreakingChanges(hashA, hashB, format: "tron");
        breakingTron.Should().Contain("$schema");

        const string symbolId = "TestProject.TestClass.MyMethod";
        var explainTron = await tools.ExplainChange(hashA, hashB, symbolId, format: "tron");
        explainTron.Should().Contain("$schema");
    }
}
