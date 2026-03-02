using System.ComponentModel;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

/// <summary>
/// MCP tool that provides a structured solution-level architecture overview from a flat SymbolGraphSnapshot.
/// Exposes: explain_solution — per-project stats, dependency DAG, stub counts, single-project detection.
/// </summary>
[McpServerToolType]
public sealed class SolutionTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SnapshotStore _snapshotStore;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<SolutionTools> _logger;
    private readonly DocAgentServerOptions _options;

    public SolutionTools(
        SnapshotStore snapshotStore,
        PathAllowlist allowlist,
        ILogger<SolutionTools> logger,
        IOptions<DocAgentServerOptions> options)
    {
        _snapshotStore = snapshotStore;
        _allowlist = allowlist;
        _logger = logger;
        _options = options.Value;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: explain_solution
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "explain_solution")]
    [Description("Get a structured solution-level architecture overview: per-project node/edge counts, doc coverage, cross-project dependency DAG, stub node count, and single-project detection.")]
    public async Task<string> ExplainSolution(
        [Description("Content hash of the solution snapshot (returned by ingest_solution)")] string snapshotHash,
        CancellationToken cancellationToken = default)
    {
        // PathAllowlist gate — deny access to snapshot store if directory is not allowed
        if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
        {
            _logger.LogWarning("SolutionTools: snapshot store directory denied by allowlist");
            return ErrorResponse("Solution not found.");
        }

        // Load snapshot
        var snapshot = await _snapshotStore.LoadAsync(snapshotHash, cancellationToken);
        if (snapshot is null)
            return ErrorResponse("Solution not found.");

        // Determine solution name
        var solutionName = snapshot.SolutionName ?? snapshot.ProjectName;

        // Group Real nodes by ProjectOrigin
        var realNodes = snapshot.Nodes
            .Where(n => n.NodeKind == NodeKind.Real)
            .ToList();

        // Collect unique project names from Real nodes
        var projectNames = realNodes
            .Select(n => n.ProjectOrigin ?? snapshot.ProjectName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Detect single-project: one unique project origin or SolutionName is null
        var isSingleProject = projectNames.Count <= 1;

        // Build per-project node id sets (for edge counting)
        var nodeProjectMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in realNodes)
        {
            var proj = node.ProjectOrigin ?? snapshot.ProjectName;
            nodeProjectMap[node.Id.Value] = proj;
        }

        // Build per-project stats
        var projectStats = projectNames.Select(projName =>
        {
            var projNodes = realNodes
                .Where(n => (n.ProjectOrigin ?? snapshot.ProjectName) == projName)
                .ToList();

            var projNodeIds = new HashSet<string>(
                projNodes.Select(n => n.Id.Value),
                StringComparer.Ordinal);

            // Edge count: IntraProject edges where From or To belongs to this project's node set
            var edgeCount = snapshot.Edges
                .Count(e => e.Scope == EdgeScope.IntraProject
                    && (projNodeIds.Contains(e.From.Value) || projNodeIds.Contains(e.To.Value)));

            // Doc coverage: public/protected/protectedInternal nodes of doc-relevant kinds
            var docCoveragePercent = ComputeDocCoverage(projNodes);

            return new
            {
                name = projName,
                nodeCount = projNodes.Count,
                edgeCount,
                docCoveragePercent,
            };
        }).ToList();

        // Build dependency DAG from CrossProject edges
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in snapshot.Edges.Where(e => e.Scope == EdgeScope.CrossProject))
        {
            if (!nodeProjectMap.TryGetValue(edge.From.Value, out var fromProject))
                continue;
            if (!nodeProjectMap.TryGetValue(edge.To.Value, out var toProject))
                continue;
            if (string.Equals(fromProject, toProject, StringComparison.Ordinal))
                continue;

            if (!adjacency.TryGetValue(fromProject, out var targets))
            {
                targets = new HashSet<string>(StringComparer.Ordinal);
                adjacency[fromProject] = targets;
            }
            targets.Add(toProject);
        }

        var dependencyDag = adjacency.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.OrderBy(v => v, StringComparer.Ordinal).ToList(),
            StringComparer.Ordinal);

        // Total stub node count across the solution
        var totalStubNodeCount = snapshot.Nodes.Count(n => n.NodeKind == NodeKind.Stub);

        // Build response
        var result = new
        {
            solutionName,
            snapshotId = snapshotHash,
            projects = projectStats,
            dependencyDag,
            totalStubNodeCount,
            isSingleProject,
        };

        return JsonSerializer.Serialize(result, s_jsonOptions);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static readonly HashSet<SymbolKind> s_docKinds = new()
    {
        SymbolKind.Type,
        SymbolKind.Method,
        SymbolKind.Property,
        SymbolKind.Constructor,
        SymbolKind.Delegate,
        SymbolKind.Event,
        SymbolKind.Field,
    };

    private static readonly HashSet<Accessibility> s_docAccessibilities = new()
    {
        Accessibility.Public,
        Accessibility.Protected,
        Accessibility.ProtectedInternal,
    };

    private static double ComputeDocCoverage(IReadOnlyList<SymbolNode> projNodes)
    {
        var docCandidates = projNodes
            .Where(n => s_docKinds.Contains(n.Kind) && s_docAccessibilities.Contains(n.Accessibility))
            .ToList();

        if (docCandidates.Count == 0)
            return 0.0;

        var documented = docCandidates.Count(n => n.Docs?.Summary is not null);
        return Math.Round((double)documented / docCandidates.Count * 100.0, 1);
    }

    private string ErrorResponse(string message)
    {
        var opaque = _options.VerboseErrors ? message : "Solution not found.";
        return JsonSerializer.Serialize(new
        {
            error = "not_found",
            message = opaque,
        }, s_jsonOptions);
    }
}
