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
    // Tool: diff_snapshots
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "diff_solution_snapshots")]
    [Description("Diff two solution snapshots at the solution level: per-project symbol changes (added/removed/modified), projects added or removed from the solution, and cross-project edge changes between snapshots.")]
    public async Task<string> DiffSnapshots(
        [Description("Before snapshot version (content hash)")] string before,
        [Description("After snapshot version (content hash)")] string after,
        CancellationToken cancellationToken = default)
    {
        // PathAllowlist gate
        if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
        {
            _logger.LogWarning("SolutionTools: snapshot store directory denied by allowlist");
            return ErrorResponse("Solution not found.");
        }

        // Load both snapshots
        var beforeSnapshot = await _snapshotStore.LoadAsync(before, cancellationToken);
        if (beforeSnapshot is null)
            return ErrorResponse("Solution not found.");

        var afterSnapshot = await _snapshotStore.LoadAsync(after, cancellationToken);
        if (afterSnapshot is null)
            return ErrorResponse("Solution not found.");

        // Group Real nodes by ProjectOrigin for both snapshots
        var beforeProjects = GroupByProject(beforeSnapshot);
        var afterProjects = GroupByProject(afterSnapshot);

        // Detect added/removed/surviving projects
        var beforeProjectNames = new HashSet<string>(beforeProjects.Keys, StringComparer.Ordinal);
        var afterProjectNames = new HashSet<string>(afterProjects.Keys, StringComparer.Ordinal);

        var projectsAdded = afterProjectNames
            .Except(beforeProjectNames, StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var projectsRemoved = beforeProjectNames
            .Except(afterProjectNames, StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var survivingProjects = beforeProjectNames
            .Intersect(afterProjectNames, StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        // Per-project diffs for surviving projects
        var projectDiffs = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var projectName in survivingProjects)
        {
            var beforeProjectSnapshot = ExtractProjectSnapshot(beforeSnapshot, projectName, beforeProjects[projectName]);
            var afterProjectSnapshot = ExtractProjectSnapshot(afterSnapshot, projectName, afterProjects[projectName]);

            var diff = SymbolGraphDiffer.Diff(beforeProjectSnapshot, afterProjectSnapshot);

            var changes = diff.Changes.Select(c => new
            {
                changeType = c.ChangeType.ToString(),
                category = c.Category.ToString(),
                severity = c.Severity.ToString(),
                symbolId = c.SymbolId.Value,
                description = c.Description,
            }).ToList();

            projectDiffs[projectName] = new
            {
                added = diff.Summary.Added,
                removed = diff.Summary.Removed,
                modified = diff.Summary.Modified,
                changes,
            };
        }

        // Cross-project edge diff
        var beforeCrossEdges = beforeSnapshot.Edges
            .Where(e => e.Scope == EdgeScope.CrossProject)
            .ToList();

        var afterCrossEdges = afterSnapshot.Edges
            .Where(e => e.Scope == EdgeScope.CrossProject)
            .ToList();

        var beforeEdgeKeys = new HashSet<(string From, string To, SymbolEdgeKind Kind)>(
            beforeCrossEdges.Select(e => (e.From.Value, e.To.Value, e.Kind)));

        var afterEdgeKeys = new HashSet<(string From, string To, SymbolEdgeKind Kind)>(
            afterCrossEdges.Select(e => (e.From.Value, e.To.Value, e.Kind)));

        // Build node→project maps for attribution
        var beforeNodeProject = BuildNodeProjectMap(beforeSnapshot);
        var afterNodeProject = BuildNodeProjectMap(afterSnapshot);

        var addedCrossEdges = afterCrossEdges
            .Where(e => !beforeEdgeKeys.Contains((e.From.Value, e.To.Value, e.Kind)))
            .Select(e => new
            {
                from = FormatEdgeEndpoint(e.From.Value, afterNodeProject),
                to = FormatEdgeEndpoint(e.To.Value, afterNodeProject),
                kind = e.Kind.ToString(),
            })
            .ToList();

        var removedCrossEdges = beforeCrossEdges
            .Where(e => !afterEdgeKeys.Contains((e.From.Value, e.To.Value, e.Kind)))
            .Select(e => new
            {
                from = FormatEdgeEndpoint(e.From.Value, beforeNodeProject),
                to = FormatEdgeEndpoint(e.To.Value, beforeNodeProject),
                kind = e.Kind.ToString(),
            })
            .ToList();

        // Build response
        var result = new
        {
            before,
            after,
            projectsAdded,
            projectsRemoved,
            projectDiffs,
            crossProjectEdgeChanges = new
            {
                added = addedCrossEdges,
                removed = removedCrossEdges,
            },
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

    private static Dictionary<string, List<SymbolNode>> GroupByProject(SymbolGraphSnapshot snapshot)
    {
        var result = new Dictionary<string, List<SymbolNode>>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
        {
            var proj = node.ProjectOrigin ?? snapshot.ProjectName;
            if (!result.TryGetValue(proj, out var list))
                result[proj] = list = new List<SymbolNode>();
            list.Add(node);
        }
        return result;
    }

    private static SymbolGraphSnapshot ExtractProjectSnapshot(
        SymbolGraphSnapshot flat,
        string projectName,
        IReadOnlyList<SymbolNode> nodes)
    {
        var nodeIdSet = new HashSet<string>(
            nodes.Select(n => n.Id.Value),
            StringComparer.Ordinal);

        var intraEdges = flat.Edges
            .Where(e => e.Scope == EdgeScope.IntraProject
                && (nodeIdSet.Contains(e.From.Value) || nodeIdSet.Contains(e.To.Value)))
            .ToList();

        return flat with
        {
            ProjectName = projectName,
            Nodes = nodes.ToList(),
            Edges = intraEdges,
        };
    }

    private static Dictionary<string, string> BuildNodeProjectMap(SymbolGraphSnapshot snapshot)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in snapshot.Nodes.Where(n => n.NodeKind == NodeKind.Real))
            map[node.Id.Value] = node.ProjectOrigin ?? snapshot.ProjectName;
        return map;
    }

    private static string FormatEdgeEndpoint(string symbolId, Dictionary<string, string> nodeProjectMap)
    {
        if (nodeProjectMap.TryGetValue(symbolId, out var project))
            return $"{project}::{symbolId}";
        return symbolId;
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
