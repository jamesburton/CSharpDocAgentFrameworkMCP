using System.Text.Json;
using DocAgent.Core;
using DocAgent.Indexing;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Tools;
using FluentAssertions;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

/// <summary>
/// Cross-tool verification tests for all 14 MCP tools against TypeScript snapshots.
/// Exercises: 7 query tools (DocTools), 3 change tools (ChangeTools), 2 solution tools (SolutionTools),
/// plus 2 C# coexistence regression tests.
/// No real TypeScript compiler / sidecar needed — in-memory SymbolGraphSnapshot construction.
/// </summary>
public sealed class TypeScriptToolVerificationTests : IDisposable
{
    // ─────────────────────────────────────────────────────────────────
    // Fixture fields
    // ─────────────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly SnapshotStore _store;
    private readonly BM25SearchIndex _index;
    private readonly KnowledgeQueryService _queryService;
    private readonly DocTools _docTools;
    private readonly ChangeTools _changeTools;
    private readonly SolutionTools _solutionTools;

    // TypeScript snapshot hashes
    private readonly string _hashA;
    private readonly string _hashB;

    // C# snapshot hash and solution hash for regression tests
    private readonly string _csharpSnapshotHash;
    private readonly string _solutionHashBefore;
    private readonly string _solutionHashAfter;

    // Key symbol IDs for assertions
    private readonly string _myServiceId;
    private readonly string _iServiceId;

    // ─────────────────────────────────────────────────────────────────
    // Constructor / fixture setup
    // ─────────────────────────────────────────────────────────────────

    public TypeScriptToolVerificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TypeScriptToolVerificationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _store = new SnapshotStore(_tempDir);
        // Use RAMDirectory (injected mode) for search — avoids FSDirectory per-snapshot swapping
        // which only keeps the last indexed snapshot's directory active.
        _index = new BM25SearchIndex(new LuceneRAMDirectory());

        // ── Build TypeScript Snapshot A ────────────────────────────────

        var namespaceId = new SymbolId("T:ts-project:index.ts:");
        var iServiceId  = new SymbolId("T:ts-project:index.ts:IService");
        var myServiceId = new SymbolId("T:ts-project:index.ts:MyService");
        var handleReqId = new SymbolId("M:ts-project:index.ts:MyService.handleRequest");
        var createSrvId = new SymbolId("M:ts-project:index.ts:createServer");

        _iServiceId  = iServiceId.Value;
        _myServiceId = myServiceId.Value;

        var nodesA = new List<SymbolNode>
        {
            new(namespaceId, SymbolKind.Namespace, "index.ts", "ts-project.index.ts",
                [], Accessibility.Public, null, null, null, [], [], "ts-project"),

            new(iServiceId, SymbolKind.Type, "IService", "ts-project.IService",
                [], Accessibility.Public,
                new DocComment("Service interface", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], []),
                null, null, [], [], "ts-project"),

            new(myServiceId, SymbolKind.Type, "MyService", "ts-project.MyService",
                [], Accessibility.Public,
                new DocComment("Service implementation", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], []),
                null, null, [], [], "ts-project"),

            new(handleReqId, SymbolKind.Method, "handleRequest", "ts-project.MyService.handleRequest",
                [], Accessibility.Public,
                new DocComment("Handles a request", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), "Promise<void>", [], [], []),
                null, "Promise<void>",
                [new ParameterInfo("req", "Request", null, false, false, false, false)],
                [], "ts-project"),

            new(createSrvId, SymbolKind.Method, "createServer", "ts-project.createServer",
                [], Accessibility.Public,
                new DocComment("Creates a server", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], []),
                null, null, [], [], "ts-project"),
        };

        var edgesA = new List<SymbolEdge>
        {
            new(namespaceId, iServiceId,  SymbolEdgeKind.Contains,    EdgeScope.IntraProject),
            new(namespaceId, myServiceId, SymbolEdgeKind.Contains,    EdgeScope.IntraProject),
            new(myServiceId, handleReqId, SymbolEdgeKind.Contains,    EdgeScope.IntraProject),
            new(myServiceId, iServiceId,  SymbolEdgeKind.Implements,  EdgeScope.IntraProject),
            new(createSrvId, myServiceId, SymbolEdgeKind.References,  EdgeScope.IntraProject),
        };

        var rawSnapshotA = new SymbolGraphSnapshot(
            "1.0", "ts-project", "ts-fingerprint-a", null, DateTimeOffset.UtcNow, nodesA, edgesA);

        // ── Build TypeScript Snapshot B (handleRequest removed, processEvent added, doc changed) ──

        var processEventId = new SymbolId("M:ts-project:index.ts:MyService.processEvent");

        var nodesB = nodesA
            .Where(n => n.Id != handleReqId)
            .Select(n =>
            {
                if (n.Id == myServiceId)
                    return n with
                    {
                        Docs = new DocComment("Updated service implementation", null,
                            new Dictionary<string, string>(), new Dictionary<string, string>(),
                            null, [], [], [])
                    };
                return n;
            })
            .Append(new SymbolNode(
                processEventId, SymbolKind.Method, "processEvent", "ts-project.MyService.processEvent",
                [], Accessibility.Public,
                new DocComment("Processes an event", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], []),
                null, null, [], [], "ts-project"))
            .ToList();

        var edgesB = new List<SymbolEdge>
        {
            new(namespaceId, iServiceId,    SymbolEdgeKind.Contains,   EdgeScope.IntraProject),
            new(namespaceId, myServiceId,   SymbolEdgeKind.Contains,   EdgeScope.IntraProject),
            new(myServiceId, processEventId,SymbolEdgeKind.Contains,   EdgeScope.IntraProject),
            new(myServiceId, iServiceId,    SymbolEdgeKind.Implements, EdgeScope.IntraProject),
            new(createSrvId, myServiceId,   SymbolEdgeKind.References, EdgeScope.IntraProject),
        };

        var rawSnapshotB = new SymbolGraphSnapshot(
            "1.0", "ts-project", "ts-fingerprint-b", null, DateTimeOffset.UtcNow.AddMinutes(1), nodesB, edgesB);

        // ── Build a minimal C# snapshot ───────────────────────────────

        var csClassId  = new SymbolId("DocAgentCSharp::CSharpProject.CsClass");
        var csMethodId = new SymbolId("DocAgentCSharp::CSharpProject.CsClass.CsMethod");

        var csNodes = new List<SymbolNode>
        {
            new(csClassId, SymbolKind.Type, "CsClass", "CSharpProject.CsClass",
                [], Accessibility.Public,
                new DocComment("A C# class", null, new Dictionary<string, string>(),
                    new Dictionary<string, string>(), null, [], [], []),
                null, null, [], [], "CSharpProject"),

            new(csMethodId, SymbolKind.Method, "CsMethod", "CSharpProject.CsClass.CsMethod",
                [], Accessibility.Public, null, null, "void", [], [], "CSharpProject"),
        };

        var csSnapshot = new SymbolGraphSnapshot(
            "1.0", "CSharpProject", "cs-fingerprint", null, DateTimeOffset.UtcNow.AddMinutes(2), csNodes, []);

        // ── Build two solution snapshots (before/after) ───────────────

        // Solution snapshot A = same nodes as TS snapshot A merged with C# nodes
        var solNodesA = new List<SymbolNode>(nodesA);
        solNodesA.AddRange(csNodes);

        var solSnapshotA = new SymbolGraphSnapshot(
            "1.0", "MyTestSolution", "sol-fingerprint-a", null, DateTimeOffset.UtcNow.AddMinutes(3),
            solNodesA, edgesA, SolutionName: "MyTestSolution");

        // Solution snapshot B = same as TS snapshot B + C# nodes (minor diff: processEvent added)
        var solNodesB = new List<SymbolNode>(nodesB);
        solNodesB.AddRange(csNodes);

        var solSnapshotB = new SymbolGraphSnapshot(
            "1.0", "MyTestSolution", "sol-fingerprint-b", null, DateTimeOffset.UtcNow.AddMinutes(4),
            solNodesB, edgesB, SolutionName: "MyTestSolution");

        // ── Save all snapshots to store synchronously ──────────────────

        var savedA    = _store.SaveAsync(rawSnapshotA).GetAwaiter().GetResult();
        var savedB    = _store.SaveAsync(rawSnapshotB).GetAwaiter().GetResult();
        var savedCs   = _store.SaveAsync(csSnapshot).GetAwaiter().GetResult();
        var savedSolA = _store.SaveAsync(solSnapshotA).GetAwaiter().GetResult();
        var savedSolB = _store.SaveAsync(solSnapshotB).GetAwaiter().GetResult();

        _hashA              = savedA.ContentHash!;
        _hashB              = savedB.ContentHash!;
        _csharpSnapshotHash = savedCs.ContentHash!;
        _solutionHashBefore = savedSolA.ContentHash!;
        _solutionHashAfter  = savedSolB.ContentHash!;

        // ── Index ONLY the combined sol B snapshot for the query service ──
        // Sol B (latest, UtcNow+4min) contains TS B nodes + CS nodes.
        // KnowledgeQueryService.ResolveSnapshotAsync picks the latest manifest entry (sol B).
        // The BM25 index (RAMDirectory) is indexed with sol B so text search works.
        // Individual TS A/B snapshots are in the store for change-tool tests (ReviewChanges, etc.).
        _index.IndexAsync(savedSolB, CancellationToken.None).GetAwaiter().GetResult();

        // ── Wire services & tools ────────────────────────────────────

        _queryService = new KnowledgeQueryService(_index, _store);

        var opts = new DocAgentServerOptions { AllowedPaths = ["**"] };
        var optionsWrapper = Options.Create(opts);
        var allowlist = new PathAllowlist(optionsWrapper);

        _docTools = new DocTools(
            _queryService,
            allowlist,
            NullLogger<DocTools>.Instance,
            optionsWrapper,
            _store);

        _changeTools = new ChangeTools(
            _store,
            allowlist,
            NullLogger<ChangeTools>.Instance,
            optionsWrapper);

        _solutionTools = new SolutionTools(
            _store,
            allowlist,
            NullLogger<SolutionTools>.Instance,
            optionsWrapper);
    }

    public void Dispose()
    {
        _index.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    // ─────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ─────────────────────────────────────────────────────────────────
    // Query tool tests (7)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchSymbols_FindsTypeScriptSymbol()
    {
        var json = await _docTools.SearchSymbols("service");
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse("should not return an error");
        root.TryGetProperty("results", out var results).Should().BeTrue();

        var resultIds = results.EnumerateArray()
            .Select(r => r.TryGetProperty("id", out var id) ? id.GetString() : null)
            .ToList();

        resultIds.Should().Contain(r => r == _iServiceId || r == _myServiceId,
            "search for 'service' should find IService or MyService");
    }

    [Fact]
    public async Task GetSymbol_ReturnsTypeScriptSymbolDetail()
    {
        var json = await _docTools.GetSymbol(_myServiceId);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();
        root.GetProperty("displayName").GetString().Should().Be("MyService");

        // Doc summary comes from the latest indexed snapshot (B) which has "Updated service implementation"
        root.TryGetProperty("docs", out var docs).Should().BeTrue();
        docs.ValueKind.Should().NotBe(JsonValueKind.Null, "docs should be present");
        docs.GetProperty("summary").GetString().Should()
            .NotBeNullOrEmpty("MyService has a doc summary");
    }

    [Fact]
    public async Task GetReferences_FindsTypeScriptReferences()
    {
        // createServer has a References edge pointing to MyService → MyService should appear in referencing callers
        var json = await _docTools.GetReferences(_myServiceId);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("references", out var refs).Should().BeTrue();

        // The snapshot edges include createServer->myService (References), so createServer's ID should appear as fromId
        var createSrvId = "M:ts-project:index.ts:createServer";
        var fromIds = refs.EnumerateArray()
            .Select(r => r.TryGetProperty("fromId", out var fid) ? fid.GetString() : null)
            .ToList();

        fromIds.Should().Contain(createSrvId,
            "createServer has a References edge pointing to MyService (fromId should be createServer's ID)");
    }

    [Fact]
    public async Task FindImplementations_FindsTypeScriptImplementors()
    {
        var json = await _docTools.FindImplementations(_iServiceId);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("implementations", out var impls).Should().BeTrue();

        var ids = impls.EnumerateArray()
            .Select(r => r.TryGetProperty("id", out var id) ? id.GetString() : null)
            .ToList();

        ids.Should().Contain(_myServiceId, "MyService implements IService");
    }

    [Fact]
    public async Task GetDocCoverage_IncludesTypeScriptProject()
    {
        var json = await _docTools.GetDocCoverage(project: "ts-project");
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // GetDocCoverage returns: { totalCandidates, totalDocumented, overallCoveragePercent, byProject, byNamespace, byKind }
        root.TryGetProperty("totalCandidates", out var totalCandidates).Should().BeTrue();
        totalCandidates.GetInt32().Should().BeGreaterThan(0, "ts-project has doc-candidate symbols");

        // byProject array should contain ts-project
        root.TryGetProperty("byProject", out var byProject).Should().BeTrue();
        var hasProjectEntry = byProject.EnumerateArray()
            .Any(p => p.TryGetProperty("project", out var pName) &&
                      pName.GetString() == "ts-project");
        hasProjectEntry.Should().BeTrue("ts-project should appear in byProject array");
    }

    [Fact]
    public async Task DiffSnapshots_DetectsTypeScriptChanges()
    {
        var json = await _docTools.DiffSnapshots(_hashA, _hashB);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // DiffSnapshots (via KnowledgeQueryService.DiffAsync) returns:
        // { snapshotVersion, timestamp, versionA, versionB, total, entries: [{id, changeKind, summary}] }
        root.TryGetProperty("entries", out var entries).Should().BeTrue();
        entries.ValueKind.Should().Be(JsonValueKind.Array);

        var allEntries = entries.EnumerateArray().ToList();
        allEntries.Should().NotBeEmpty("there are differences between snapshot A and B");

        // processEvent should appear as Added
        var hasProcessEventAdded = allEntries.Any(c =>
            c.TryGetProperty("id", out var sid) &&
            sid.GetString()!.Contains("processEvent") &&
            c.TryGetProperty("changeKind", out var ck) &&
            ck.GetString() == "Added");

        hasProcessEventAdded.Should().BeTrue("processEvent was added in snapshot B");

        // handleRequest should appear as Removed
        var hasHandleRequestRemoved = allEntries.Any(c =>
            c.TryGetProperty("id", out var sid) &&
            sid.GetString()!.Contains("handleRequest") &&
            c.TryGetProperty("changeKind", out var ck) &&
            ck.GetString() == "Removed");

        hasHandleRequestRemoved.Should().BeTrue("handleRequest was removed in snapshot B");
    }

    [Fact]
    public async Task ExplainProject_IncludesTypeScriptOverview()
    {
        var json = await _docTools.ExplainProject(chainedEntityDepth: 0);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // ExplainProject returns: { namespaces, types, stats: { totalSymbols, byKind }, promptInjectionWarning, chainedEntityDepth }
        root.ValueKind.Should().Be(JsonValueKind.Object,
            "ExplainProject should return a JSON object");

        root.TryGetProperty("stats", out var stats).Should().BeTrue();
        stats.TryGetProperty("totalSymbols", out var totalSymbols).Should().BeTrue();
        totalSymbols.GetInt32().Should().BeGreaterThan(0, "there should be symbols in the TypeScript project");
    }

    // ─────────────────────────────────────────────────────────────────
    // Change tool tests (3)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewChanges_GroupsTypeScriptChanges()
    {
        var json = await _changeTools.ReviewChanges(_hashA, _hashB);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // ReviewChanges returns: { beforeVersion, afterVersion, summary, findings, unusualFindings }
        root.TryGetProperty("findings", out var findings).Should().BeTrue();
        findings.ValueKind.Should().Be(JsonValueKind.Array);

        root.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task FindBreakingChanges_DetectsRemovedPublicMethod()
    {
        var json = await _changeTools.FindBreakingChanges(_hashA, _hashB);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // FindBreakingChanges returns: { beforeVersion, afterVersion, breakingCount, breakingChanges: [{symbolId, displayName, description}] }
        root.TryGetProperty("breakingChanges", out var breaking).Should().BeTrue();
        breaking.ValueKind.Should().Be(JsonValueKind.Array);

        var hasHandleRequestBreaking = breaking.EnumerateArray().Any(c =>
            c.TryGetProperty("symbolId", out var sid) &&
            sid.GetString()!.Contains("handleRequest"));

        hasHandleRequestBreaking.Should().BeTrue(
            "Removing public method handleRequest is a breaking change");
    }

    [Fact]
    public async Task ExplainChange_DescribesModifiedSymbol()
    {
        var json = await _changeTools.ExplainChange(_hashA, _hashB, _myServiceId);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // ExplainChange returns: { symbolId, displayName, changeType, changes: [{category, severity, description, before, after, ...}] }
        root.TryGetProperty("symbolId", out var sid).Should().BeTrue();
        sid.GetString().Should().Be(_myServiceId);

        root.TryGetProperty("changes", out var changes).Should().BeTrue();
        changes.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ─────────────────────────────────────────────────────────────────
    // C# / Solution regression tests (4)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestProject_WorksAlongsideTypeScriptSnapshots()
    {
        // A C# snapshot was saved during fixture setup; verify SearchSymbols returns its symbols
        var json = await _docTools.SearchSymbols("CsClass", project: "CSharpProject");
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();
        root.TryGetProperty("results", out var results).Should().BeTrue();

        var resultIds = results.EnumerateArray()
            .Select(r => r.TryGetProperty("id", out var id) ? id.GetString() : null)
            .ToList();

        resultIds.Should().Contain(r => r != null && r.Contains("CsClass"),
            "C# CsClass should be searchable when TypeScript snapshots coexist");
    }

    [Fact]
    public async Task IngestSolution_WorksAlongsideTypeScriptSnapshots()
    {
        // Verify the solution snapshot saved in fixture is loadable
        var loaded = await _store.LoadAsync(_solutionHashBefore);
        loaded.Should().NotBeNull("the solution snapshot should persist correctly");
        loaded!.SolutionName.Should().Be("MyTestSolution");
        loaded.Nodes.Should().NotBeEmpty("solution snapshot should contain nodes from both projects");
    }

    [Fact]
    public async Task ExplainSolution_ReturnsValidStructure()
    {
        var json = await _solutionTools.ExplainSolution(_solutionHashBefore);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        root.TryGetProperty("solutionName", out var solutionName).Should().BeTrue();
        solutionName.GetString().Should().Be("MyTestSolution");

        root.TryGetProperty("projects", out var projects).Should().BeTrue();
        projects.ValueKind.Should().Be(JsonValueKind.Array);
        projects.GetArrayLength().Should().BeGreaterThan(0,
            "solution should have at least one project entry");
    }

    [Fact]
    public async Task DiffSolutionSnapshots_WorksWithMixedStore()
    {
        var json = await _solutionTools.DiffSnapshots(_solutionHashBefore, _solutionHashAfter);
        var root = Parse(json);

        root.TryGetProperty("error", out _).Should().BeFalse();

        // Verify the diff response has the expected top-level structure
        root.TryGetProperty("before", out var before).Should().BeTrue();
        before.GetString().Should().Be(_solutionHashBefore);

        root.TryGetProperty("after", out var after).Should().BeTrue();
        after.GetString().Should().Be(_solutionHashAfter);

        root.TryGetProperty("projectDiffs", out _).Should().BeTrue();
    }
}
