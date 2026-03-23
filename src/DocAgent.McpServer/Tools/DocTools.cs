using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.Ingestion;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Serialization;
using DocAgent.McpServer.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

/// <summary>
/// All five MCP tool handlers wired to IKnowledgeQueryService.
/// Security checks (path allowlist, prompt injection scanning) applied per tool.
/// </summary>
[McpServerToolType]
public sealed class DocTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<SymbolKind> s_docKinds = new()
    {
        SymbolKind.Type, SymbolKind.Method, SymbolKind.Property,
        SymbolKind.Constructor, SymbolKind.Delegate, SymbolKind.Event, SymbolKind.Field,
    };

    private static readonly HashSet<Accessibility> s_docAccessibilities = new()
    {
        Accessibility.Public, Accessibility.Protected, Accessibility.ProtectedInternal,
    };

    private readonly IKnowledgeQueryService _query;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<DocTools> _logger;
    private readonly DocAgentServerOptions _options;
    private readonly SnapshotStore _snapshotStore;

    public DocTools(
        IKnowledgeQueryService query,
        PathAllowlist allowlist,
        ILogger<DocTools> logger,
        IOptions<DocAgentServerOptions> options,
        SnapshotStore snapshotStore)
    {
        _query = query;
        _allowlist = allowlist;
        _logger = logger;
        _options = options.Value;
        _snapshotStore = snapshotStore;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 1: search_symbols  (MCPS-01)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "search_symbols"), Description("Search symbols and documentation by keyword.")]
    public async Task<string> SearchSymbols(
        [Description("Search query")] string query,
        [Description("Symbol kind filter (optional): Namespace, Type, Method, Property, Field, Event, Parameter, Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter")] string? kindFilter = null,
        [Description("Optional project name filter (exact match, case-sensitive). Omit for all projects.")] string? project = null,
        [Description("Result offset for pagination")] int offset = 0,
        [Description("Result limit (max 100)")] int limit = 20,
        [Description("Include full doc comments in results")] bool fullDocs = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.search_symbols", ActivityKind.Internal);
        activity?.SetTag("tool.name", "search_symbols");
        if (DocAgentTelemetry.VerboseMode)
            activity?.SetTag("tool.input.query", query);

        try
        {
        // Parse optional kind filter
        SymbolKind? kind = null;
        if (kindFilter is not null)
        {
            if (!Enum.TryParse<SymbolKind>(kindFilter, ignoreCase: true, out var parsedKind))
                return ErrorResponse(QueryErrorKind.InvalidInput, $"Unknown symbol kind '{kindFilter}'. Valid values: {string.Join(", ", Enum.GetNames<SymbolKind>())}");
            kind = parsedKind;
        }

        var result = await _query.SearchAsync(
            query, kind, offset, Math.Min(limit, 100),
            projectFilter: project, ct: cancellationToken);

        if (!result.Success)
            return ErrorResponse(result.Error!.Value, result.ErrorMessage);

        var envelope = result.Value!;
        var items = envelope.Payload;

        // Prompt injection scanning on snippets
        bool hasInjectionWarning = false;
        var sanitizedItems = items.Select(item =>
        {
            var (sanitizedSnippet, snippetWarn) = PromptInjectionScanner.Scan(item.Snippet);
            if (snippetWarn) hasInjectionWarning = true;
            return item with { Snippet = sanitizedSnippet };
        }).ToList();

        activity?.SetTag("tool.result_count", sanitizedItems.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var payload = new
            {
                snapshotVersion = envelope.SnapshotVersion,
                timestamp = envelope.Timestamp,
                isStale = envelope.IsStale,
                queryDuration = envelope.QueryDuration.TotalMilliseconds,
                promptInjectionWarning = hasInjectionWarning,
                total = sanitizedItems.Count,
                offset,
                limit = Math.Min(limit, 100),
                results = fullDocs
                    ? (object)sanitizedItems
                    : sanitizedItems.Select(i => new { id = i.Id.Value, score = i.Score, kind = i.Kind.ToString(), displayName = i.DisplayName, snippet = i.Snippet, projectName = i.ProjectName }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeSearchResults(sanitizedItems),
        () => RenderSearchMarkdown(sanitizedItems, envelope));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 2: get_symbol  (MCPS-02)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_symbol"), Description("Get full symbol detail by stable SymbolId.")]
    public async Task<string> GetSymbol(
        [Description("Stable SymbolId (assembly-qualified)")] string symbolId,
        [Description("Include source file path and line range")] bool includeSourceSpans = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.get_symbol", ActivityKind.Internal);
        activity?.SetTag("tool.name", "get_symbol");
        if (DocAgentTelemetry.VerboseMode)
            activity?.SetTag("tool.input.symbolId", symbolId);

        try
        {
        if (string.IsNullOrWhiteSpace(symbolId))
            return ErrorResponse(QueryErrorKind.InvalidInput, "symbolId is required.");

        // FQN disambiguation: if input does not look like a stable SymbolId (no '|' separator),
        // attempt to resolve it as a fully qualified name.
        if (!symbolId.Contains('|'))
        {
            var fqnResult = await ResolveByFqnAsync(symbolId, cancellationToken);
            if (fqnResult.IsAmbiguous)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "AmbiguousFqn");
                var projectList = string.Join(", ", fqnResult.Projects!.Select(p => $"'{p}'"));
                return ErrorResponse(QueryErrorKind.InvalidInput,
                    $"Ambiguous FQN '{symbolId}' found in multiple projects: {projectList}. Specify the stable SymbolId or use search_symbols with project filter to disambiguate.");
            }
            if (fqnResult.ResolvedId.HasValue)
            {
                var id2 = fqnResult.ResolvedId.Value;
                var result2 = await _query.GetSymbolAsync(id2, ct: cancellationToken);
                if (!result2.Success)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "NotFound");
                    return ErrorResponse(result2.Error!.Value, result2.ErrorMessage);
                }

                var envelope2 = result2.Value!;
                var detail2 = envelope2.Payload;

                bool spansRedacted2 = false;
                if (includeSourceSpans && detail2.Node.Span is not null)
                {
                    if (!_allowlist.IsAllowed(detail2.Node.Span.FilePath))
                    {
                        spansRedacted2 = true;
                        _logger.LogDebug("Source span for {SymbolId} redacted: path outside allowlist", id2.Value);
                    }
                }

                bool hasInjectionWarning2 = false;
                string? sanitizedSummary2 = null;
                if (detail2.Node.Docs?.Summary is not null)
                {
                    var (sanitized2, warn2) = PromptInjectionScanner.Scan(detail2.Node.Docs.Summary);
                    sanitizedSummary2 = sanitized2;
                    hasInjectionWarning2 = warn2;
                }

                activity?.SetTag("tool.result_count", 1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return FormatResponse(format, () =>
                {
                    var node2 = detail2.Node;
                    var payload2 = new
                    {
                        snapshotVersion = envelope2.SnapshotVersion,
                        promptInjectionWarning = hasInjectionWarning2,
                        id = node2.Id.Value,
                        kind = node2.Kind.ToString(),
                        displayName = node2.DisplayName,
                        fullyQualifiedName = node2.FullyQualifiedName,
                        accessibility = node2.Accessibility.ToString(),
                        docs = sanitizedSummary2 is not null
                            ? (object)new { summary = sanitizedSummary2, remarks = node2.Docs?.Remarks, returns = node2.Docs?.Returns }
                            : null,
                        span = (includeSourceSpans && !spansRedacted2) ? node2.Span : null,
                        spansRedacted = spansRedacted2,
                        parentId = detail2.ParentId?.Value,
                        childIds = detail2.ChildIds.Select(c => c.Value).ToList(),
                        relatedIds = detail2.RelatedIds.Select(r => r.Value).ToList(),
                    };
                    return JsonSerializer.Serialize(payload2, s_jsonOptions);
                },
                () => TronSerializer.SerializeSymbolDetail(detail2),
                () => RenderSymbolMarkdown(detail2, sanitizedSummary2, includeSourceSpans && !spansRedacted2));
            }
            // 0 matches from FQN scan — fall through to normal SymbolId resolution (will return NotFound)
        }

        var id = new SymbolId(symbolId);
        var result = await _query.GetSymbolAsync(id, ct: cancellationToken);

        if (!result.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "NotFound");
            return ErrorResponse(result.Error!.Value, result.ErrorMessage);
        }

        var envelope = result.Value!;
        var detail = envelope.Payload;

        // Path allowlist check for source spans
        bool spansRedacted = false;
        if (includeSourceSpans && detail.Node.Span is not null)
        {
            if (!_allowlist.IsAllowed(detail.Node.Span.FilePath))
            {
                spansRedacted = true;
                _logger.LogDebug("Source span for {SymbolId} redacted: path outside allowlist", symbolId);
            }
        }

        // Prompt injection scanning on doc comment
        bool hasInjectionWarning = false;
        string? sanitizedSummary = null;
        if (detail.Node.Docs?.Summary is not null)
        {
            var (sanitized, warn) = PromptInjectionScanner.Scan(detail.Node.Docs.Summary);
            sanitizedSummary = sanitized;
            hasInjectionWarning = warn;
        }

        activity?.SetTag("tool.result_count", 1);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var node = detail.Node;
            var payload = new
            {
                snapshotVersion = envelope.SnapshotVersion,
                promptInjectionWarning = hasInjectionWarning,
                id = node.Id.Value,
                kind = node.Kind.ToString(),
                displayName = node.DisplayName,
                fullyQualifiedName = node.FullyQualifiedName,
                accessibility = node.Accessibility.ToString(),
                docs = sanitizedSummary is not null
                    ? (object)new { summary = sanitizedSummary, remarks = node.Docs?.Remarks, returns = node.Docs?.Returns }
                    : null,
                span = (includeSourceSpans && !spansRedacted) ? node.Span : null,
                spansRedacted,
                parentId = detail.ParentId?.Value,
                childIds = detail.ChildIds.Select(c => c.Value).ToList(),
                relatedIds = detail.RelatedIds.Select(r => r.Value).ToList(),
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeSymbolDetail(detail),
        () => RenderSymbolMarkdown(detail, sanitizedSummary, includeSourceSpans && !spansRedacted));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 3: get_references  (MCPS-03)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_references"), Description("Get symbols that reference the given symbol.")]
    public async Task<string> GetReferences(
        [Description("Stable SymbolId")] string symbolId,
        [Description("When true, returns only cross-project edges (EdgeScope.CrossProject).")] bool crossProjectOnly = false,
        [Description("Include surrounding code context (not yet implemented)")] bool includeContext = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        [Description("Result offset for pagination (default: return all)")] int offset = 0,
        [Description("Result limit; 0 = return all (default), max 200")] int limit = 0,
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.get_references", ActivityKind.Internal);
        activity?.SetTag("tool.name", "get_references");
        if (DocAgentTelemetry.VerboseMode)
            activity?.SetTag("tool.input.symbolId", symbolId);

        try
        {
        if (string.IsNullOrWhiteSpace(symbolId))
            return ErrorResponse(QueryErrorKind.InvalidInput, "symbolId is required.");

        var id = new SymbolId(symbolId);

        // Buffer the async enumerable into a list
        var edges = new List<SymbolEdge>();
        try
        {
            await foreach (var edge in _query.GetReferencesAsync(id, crossProjectOnly, cancellationToken))
                edges.Add(edge);
        }
        catch (SymbolNotFoundException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "NotFound");
            return ErrorResponse(QueryErrorKind.NotFound, $"Symbol '{symbolId}' not found.");
        }

        bool hasInjectionWarning = false; // edges have no doc content

        // Resolve project names for each unique From/To SymbolId
        var uniqueIds = edges.SelectMany(e => new[] { e.From, e.To }).Distinct().ToList();
        var nodeProjectCache = new Dictionary<SymbolId, string?>();
        foreach (var nodeId in uniqueIds)
        {
            var nodeResult = await _query.GetSymbolAsync(nodeId, ct: cancellationToken);
            if (nodeResult.Success)
                nodeProjectCache[nodeId] = nodeResult.Value!.Payload.Node.ProjectOrigin;
        }

        // Apply pagination
        var totalCount = edges.Count;
        var paginatedEdges = limit > 0
            ? edges.Skip(offset).Take(Math.Min(limit, 200)).ToList()
            : edges;

        activity?.SetTag("tool.result_count", paginatedEdges.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var payload = new
            {
                promptInjectionWarning = hasInjectionWarning,
                total = paginatedEdges.Count,
                totalCount = totalCount,
                offset = offset,
                limit = limit > 0 ? Math.Min(limit, 200) : totalCount,
                references = paginatedEdges.Select(e => new
                {
                    fromId = e.From.Value,
                    toId = e.To.Value,
                    edgeKind = e.Kind.ToString(),
                    scope = e.Scope.ToString(),
                    fromProject = nodeProjectCache.GetValueOrDefault(e.From),
                    toProject = nodeProjectCache.GetValueOrDefault(e.To),
                }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeReferences(paginatedEdges),
        () => RenderReferencesMarkdown(paginatedEdges));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: find_implementations
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "find_implementations"), Description("Find all types implementing a given interface or deriving from a base class.")]
    public async Task<string> FindImplementations(
        [Description("Stable SymbolId of the interface or base class")] string symbolId,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.find_implementations", ActivityKind.Internal);
        activity?.SetTag("tool.name", "find_implementations");
        if (DocAgentTelemetry.VerboseMode)
            activity?.SetTag("tool.input.symbolId", symbolId);

        try
        {
        if (string.IsNullOrWhiteSpace(symbolId))
            return ErrorResponse(QueryErrorKind.InvalidInput, "symbolId is required.");

        var id = new SymbolId(symbolId);

        // Collect ALL references for this symbol
        var allEdges = new List<SymbolEdge>();
        try
        {
            await foreach (var edge in _query.GetReferencesAsync(id, crossProjectOnly: false, cancellationToken))
                allEdges.Add(edge);
        }
        catch (SymbolNotFoundException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "NotFound");
            return ErrorResponse(QueryErrorKind.NotFound, $"Symbol '{symbolId}' not found.");
        }

        // Filter to Implements/Inherits edges pointing TO this symbol
        var implEdges = allEdges
            .Where(e => (e.Kind == SymbolEdgeKind.Implements || e.Kind == SymbolEdgeKind.Inherits) && e.To == id)
            .ToList();

        // Resolve implementing nodes, excluding stubs
        var implementations = new List<SymbolNode>();
        foreach (var edge in implEdges)
        {
            var nodeResult = await _query.GetSymbolAsync(edge.From, ct: cancellationToken);
            if (nodeResult.Success)
            {
                var node = nodeResult.Value!.Payload.Node;
                if (node.NodeKind != NodeKind.Stub)
                    implementations.Add(node);
            }
        }

        activity?.SetTag("tool.result_count", implementations.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var payload = new
            {
                symbolId = symbolId,
                totalCount = implementations.Count,
                implementations = implementations.Select(n => new
                {
                    id = n.Id.Value,
                    displayName = n.DisplayName,
                    kind = n.Kind.ToString(),
                    fullyQualifiedName = n.FullyQualifiedName,
                    projectOrigin = n.ProjectOrigin,
                }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FIND_IMPLEMENTATIONS");
            sb.AppendLine($"  symbolId: {symbolId}");
            sb.AppendLine($"  totalCount: {implementations.Count}");
            foreach (var n in implementations)
                sb.AppendLine($"  - {n.Id.Value} ({n.Kind}) {n.DisplayName}");
            return sb.ToString();
        },
        () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Implementations");
            sb.AppendLine($"**Symbol:** `{symbolId}`");
            sb.AppendLine($"**Total:** {implementations.Count}");
            sb.AppendLine();
            foreach (var n in implementations)
                sb.AppendLine($"- `{n.Id.Value}` — {n.DisplayName} ({n.Kind})");
            return sb.ToString();
        });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: get_doc_coverage
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_doc_coverage"), Description("Get documentation coverage metrics grouped by project, namespace, and symbol kind.")]
    public async Task<string> GetDocCoverage(
        [Description("Optional project name filter (exact match). Omit for all projects.")] string? project = null,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.get_doc_coverage", ActivityKind.Internal);
        activity?.SetTag("tool.name", "get_doc_coverage");

        try
        {
        // Load the latest snapshot directly for efficient full-graph traversal.
        // Previous approach searched via wildcard then resolved each node individually,
        // which was O(N) GetSymbolAsync calls over 252K+ nodes — prohibitively slow.
        var snapshot = await _snapshotStore.LoadLatestAsync(cancellationToken);
        if (snapshot is null)
            return ErrorResponse(QueryErrorKind.SnapshotMissing, "No snapshots available.");

        // Filter to Real nodes, optionally by project
        var realNodes = snapshot.Nodes
            .Where(n => n.NodeKind == NodeKind.Real)
            .Where(n => project is null || n.ProjectOrigin == project)
            .ToList();

        // Step 5: Filter to doc candidates
        var candidates = realNodes
            .Where(n => s_docKinds.Contains(n.Kind) && s_docAccessibilities.Contains(n.Accessibility))
            .ToList();

        // Step 6: Group three ways
        var byProject = candidates
            .GroupBy(n => n.ProjectOrigin ?? "(unknown)")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                project = g.Key,
                total = g.Count(),
                documented = g.Count(n => n.Docs?.Summary is not null),
                coveragePercent = g.Count() == 0 ? 0.0 : Math.Round((double)g.Count(n => n.Docs?.Summary is not null) / g.Count() * 100.0, 1)
            })
            .ToList();

        var byNamespace = candidates
            .GroupBy(n =>
            {
                var fqn = n.FullyQualifiedName;
                if (fqn is null) return "(global)";
                var lastDot = fqn.LastIndexOf('.');
                return lastDot >= 0 ? fqn[..lastDot] : "(global)";
            })
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new
            {
                @namespace = g.Key,
                total = g.Count(),
                documented = g.Count(n => n.Docs?.Summary is not null),
                coveragePercent = g.Count() == 0 ? 0.0 : Math.Round((double)g.Count(n => n.Docs?.Summary is not null) / g.Count() * 100.0, 1)
            })
            .ToList();

        var byKind = candidates
            .GroupBy(n => n.Kind)
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .Select(g => new
            {
                kind = g.Key.ToString(),
                total = g.Count(),
                documented = g.Count(n => n.Docs?.Summary is not null),
                coveragePercent = g.Count() == 0 ? 0.0 : Math.Round((double)g.Count(n => n.Docs?.Summary is not null) / g.Count() * 100.0, 1)
            })
            .ToList();

        // Step 7: Build response
        var overallDocumented = candidates.Count(n => n.Docs?.Summary is not null);

        activity?.SetTag("tool.result_count", candidates.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var payload = new
            {
                totalCandidates = candidates.Count,
                totalDocumented = overallDocumented,
                overallCoveragePercent = candidates.Count == 0 ? 0.0 : Math.Round((double)overallDocumented / candidates.Count * 100.0, 1),
                byProject,
                byNamespace,
                byKind,
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GET_DOC_COVERAGE");
            sb.AppendLine($"  totalCandidates: {candidates.Count}");
            sb.AppendLine($"  totalDocumented: {overallDocumented}");
            foreach (var p in byProject)
                sb.AppendLine($"  project:{p.project} total:{p.total} documented:{p.documented} coverage:{p.coveragePercent}%");
            return sb.ToString();
        },
        () =>
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Documentation Coverage");
            sb.AppendLine();
            sb.AppendLine($"**Overall:** {overallDocumented}/{candidates.Count} ({(candidates.Count == 0 ? 0.0 : Math.Round((double)overallDocumented / candidates.Count * 100.0, 1))}%)");
            sb.AppendLine();
            sb.AppendLine("## By Project");
            foreach (var p in byProject)
                sb.AppendLine($"- **{p.project}**: {p.documented}/{p.total} ({p.coveragePercent}%)");
            sb.AppendLine();
            sb.AppendLine("## By Namespace");
            foreach (var ns in byNamespace)
                sb.AppendLine($"- **{ns.@namespace}**: {ns.documented}/{ns.total} ({ns.coveragePercent}%)");
            sb.AppendLine();
            sb.AppendLine("## By Kind");
            foreach (var k in byKind)
                sb.AppendLine($"- **{k.kind}**: {k.documented}/{k.total} ({k.coveragePercent}%)");
            return sb.ToString();
        });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 4: diff_snapshots  (MCPS-04)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "diff_snapshots"), Description("Diff two snapshot versions showing added/removed/modified symbols.")]
    public async Task<string> DiffSnapshots(
        [Description("Snapshot A version (content hash or 'latest')")] string versionA,
        [Description("Snapshot B version (content hash or 'latest~1' for previous)")] string versionB,
        [Description("Include inline before/after doc content")] bool includeDiffs = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.diff_snapshots", ActivityKind.Internal);
        activity?.SetTag("tool.name", "diff_snapshots");
        if (DocAgentTelemetry.VerboseMode)
        {
            activity?.SetTag("tool.input.versionA", versionA);
            activity?.SetTag("tool.input.versionB", versionB);
        }

        try
        {
        if (string.IsNullOrWhiteSpace(versionA) || string.IsNullOrWhiteSpace(versionB))
            return ErrorResponse(QueryErrorKind.InvalidInput, "versionA and versionB are required.");

        var a = new SnapshotRef(versionA);
        var b = new SnapshotRef(versionB);

        var result = await _query.DiffAsync(a, b, cancellationToken);

        if (!result.Success)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "NotFound");
            return ErrorResponse(result.Error!.Value, result.ErrorMessage);
        }

        var envelope = result.Value!;
        var diff = envelope.Payload;

        activity?.SetTag("tool.result_count", diff.Entries.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
        {
            var payload = new
            {
                snapshotVersion = envelope.SnapshotVersion,
                timestamp = envelope.Timestamp,
                versionA,
                versionB,
                total = diff.Entries.Count,
                entries = includeDiffs
                    ? (object)diff.Entries
                    : diff.Entries.Select(e => new { id = e.Id.Value, changeKind = e.ChangeKind.ToString(), summary = e.Summary }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeDiff(diff),
        () => RenderDiffMarkdown(diff, versionA, versionB));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 5: explain_project  (MCPS-05)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "explain_project"), Description("Get a comprehensive project overview in one call.")]
    public async Task<string> ExplainProject(
        [Description("Max depth for chained entity loading (0=summary only, 1=top-level types, 2+=children)")] int chainedEntityDepth = 1,
        [Description("Sections to include (comma-separated): namespaces,types,stats,dependencies")] string? includeSections = null,
        [Description("Sections to exclude (comma-separated)")] string? excludeSections = null,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.explain_project", ActivityKind.Internal);
        activity?.SetTag("tool.name", "explain_project");

        try
        {
        // Parse include/exclude section filters
        var includes = ParseSections(includeSections);
        var excludes = ParseSections(excludeSections);
        bool ShouldInclude(string section) =>
            (includes.Count == 0 || includes.Contains(section)) && !excludes.Contains(section);

        // Step 1: Discover top-level symbols via wildcard search
        var searchResult = await _query.SearchAsync(
            "*", offset: 0, limit: 100, ct: cancellationToken);

        if (!searchResult.Success)
            return ErrorResponse(searchResult.Error!.Value, searchResult.ErrorMessage);

        var allItems = searchResult.Value!.Payload;
        bool hasInjectionWarning = false;

        // Step 2: Group by namespace for namespace section
        var namespaceGroups = allItems
            .Where(i => i.Kind == SymbolKind.Namespace)
            .Select(i => i.DisplayName)
            .OrderBy(n => n)
            .ToList();

        // Step 3: Stats by kind
        var statsByKind = allItems
            .GroupBy(i => i.Kind)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Step 4: Top-level types (non-namespace symbols)
        var topLevelTypes = allItems
            .Where(i => i.Kind is SymbolKind.Type or SymbolKind.Delegate)
            .ToList();

        // Step 5: Load symbol detail for top-level types if depth >= 1
        var typeDetails = new List<object>();
        if (chainedEntityDepth >= 1 && ShouldInclude("types"))
        {
            foreach (var item in topLevelTypes.Take(20)) // cap to avoid token explosion
            {
                var detailResult = await _query.GetSymbolAsync(
                    new SymbolId(item.Id.Value), ct: cancellationToken);

                if (!detailResult.Success) continue;

                var detail = detailResult.Value!.Payload;
                var (sanitized, warn) = PromptInjectionScanner.Scan(detail.Node.Docs?.Summary);
                if (warn) hasInjectionWarning = true;

                var children = new List<object>();
                // Step 5b: recurse into children if depth >= 2
                if (chainedEntityDepth >= 2)
                {
                    foreach (var childId in detail.ChildIds.Take(10))
                    {
                        var childResult = await _query.GetSymbolAsync(
                            childId, ct: cancellationToken);
                        if (!childResult.Success) continue;
                        var child = childResult.Value!.Payload.Node;
                        var (cSan, cWarn) = PromptInjectionScanner.Scan(child.Docs?.Summary);
                        if (cWarn) hasInjectionWarning = true;
                        children.Add(new { id = child.Id.Value, kind = child.Kind.ToString(), displayName = child.DisplayName, summary = cSan });
                    }
                }

                typeDetails.Add(new
                {
                    id = detail.Node.Id.Value,
                    kind = detail.Node.Kind.ToString(),
                    displayName = detail.Node.DisplayName,
                    fullyQualifiedName = detail.Node.FullyQualifiedName,
                    summary = sanitized,
                    childCount = detail.ChildIds.Count,
                    children = chainedEntityDepth >= 2 ? children : null,
                });
            }
        }

        // Build overview object
        var overview = new Dictionary<string, object?>();
        if (ShouldInclude("namespaces")) overview["namespaces"] = namespaceGroups;
        if (ShouldInclude("types")) overview["types"] = typeDetails;
        if (ShouldInclude("stats")) overview["stats"] = new { totalSymbols = allItems.Count, byKind = statsByKind };
        overview["promptInjectionWarning"] = hasInjectionWarning;
        overview["chainedEntityDepth"] = chainedEntityDepth;

        activity?.SetTag("tool.result_count", allItems.Count);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return FormatResponse(format, () =>
            JsonSerializer.Serialize(overview, s_jsonOptions),
        () => TronSerializer.SerializeProjectOverview(overview),
        () => RenderProjectMarkdown(overview));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private sealed record FqnResolveResult(bool IsAmbiguous, SymbolId? ResolvedId, IReadOnlyList<string>? Projects);

    /// <summary>
    /// Attempts to resolve <paramref name="fqn"/> as a fully qualified name by searching and
    /// checking each candidate's FullyQualifiedName. Returns:
    /// - ResolvedId set if exactly one match.
    /// - IsAmbiguous=true if multiple projects have the same FQN.
    /// - ResolvedId=null and IsAmbiguous=false if no match (caller falls through to NotFound).
    /// </summary>
    private async Task<FqnResolveResult> ResolveByFqnAsync(string fqn, CancellationToken ct)
    {
        var searchResult = await _query.SearchAsync(fqn, limit: 100, ct: ct);
        if (!searchResult.Success)
            return new FqnResolveResult(IsAmbiguous: false, ResolvedId: null, Projects: null);

        var candidates = new List<(SymbolId Id, string? ProjectOrigin)>();
        foreach (var item in searchResult.Value!.Payload)
        {
            var detailResult = await _query.GetSymbolAsync(item.Id, ct: ct);
            if (!detailResult.Success) continue;
            var node = detailResult.Value!.Payload.Node;
            if (node.FullyQualifiedName == fqn)
                candidates.Add((node.Id, node.ProjectOrigin));
        }

        if (candidates.Count == 0)
            return new FqnResolveResult(IsAmbiguous: false, ResolvedId: null, Projects: null);

        if (candidates.Count == 1)
            return new FqnResolveResult(IsAmbiguous: false, ResolvedId: candidates[0].Id, Projects: null);

        // Multiple matches — group by project
        var projects = candidates
            .Select(c => c.ProjectOrigin ?? "(unknown)")
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (projects.Count == 1)
        {
            // Same project, multiple nodes with same FQN — return first
            return new FqnResolveResult(IsAmbiguous: false, ResolvedId: candidates[0].Id, Projects: null);
        }

        return new FqnResolveResult(IsAmbiguous: true, ResolvedId: null, Projects: projects);
    }

    private string FormatResponse(string format, Func<string> jsonFactory, Func<string> tronFactory, Func<string> markdownFactory)
    {
        return format.ToLowerInvariant() switch
        {
            "tron" => tronFactory(),
            "markdown" => markdownFactory(),
            _ => jsonFactory(),
        };
    }

    private string ErrorResponse(QueryErrorKind kind, string? message)
    {
        var code = kind switch
        {
            QueryErrorKind.NotFound => "not_found",
            QueryErrorKind.SnapshotMissing => "snapshot_not_found",
            QueryErrorKind.StaleIndex => "stale_index",
            QueryErrorKind.InvalidInput => "invalid_input",
            _ => "error"
        };

        var detail = _options.VerboseErrors ? message : null;
        var opaque = kind == QueryErrorKind.NotFound ? "Access denied" : (message ?? "Request failed");

        return JsonSerializer.Serialize(new
        {
            error = code,
            message = _options.VerboseErrors ? message : opaque,
            detail,
        }, s_jsonOptions);
    }

    private static HashSet<string> ParseSections(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => s.ToLowerInvariant())
                  .ToHashSet();
    }

    // ─────────────────────────────────────────────────────────────────
    // Markdown renderers
    // ─────────────────────────────────────────────────────────────────

    private static string RenderSearchMarkdown(List<SearchResultItem> items, ResponseEnvelope<IReadOnlyList<SearchResultItem>> envelope)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Search Results");
        sb.AppendLine($"**Snapshot:** {envelope.SnapshotVersion} | **Count:** {items.Count}");
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine($"## `{item.DisplayName}` ({item.Kind})");
            sb.AppendLine($"- **ID:** `{item.Id.Value}`");
            sb.AppendLine($"- **Score:** {item.Score:F4}");
            sb.AppendLine($"- {item.Snippet}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string RenderSymbolMarkdown(SymbolDetail detail, string? sanitizedSummary, bool includeSpan)
    {
        var node = detail.Node;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# `{node.DisplayName}` ({node.Kind})");
        sb.AppendLine($"**ID:** `{node.Id.Value}`");
        if (node.FullyQualifiedName is not null)
            sb.AppendLine($"**FQN:** `{node.FullyQualifiedName}`");
        sb.AppendLine($"**Accessibility:** {node.Accessibility}");
        if (sanitizedSummary is not null)
            sb.AppendLine($"\n{sanitizedSummary}");
        if (includeSpan && node.Span is not null)
            sb.AppendLine($"\n**Source:** `{node.Span.FilePath}` lines {node.Span.StartLine}-{node.Span.EndLine}");
        if (detail.ChildIds.Count > 0)
            sb.AppendLine($"\n**Children ({detail.ChildIds.Count}):** {string.Join(", ", detail.ChildIds.Take(10).Select(c => $"`{c.Value}`"))}");
        return sb.ToString();
    }

    private static string RenderReferencesMarkdown(List<SymbolEdge> edges)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# References");
        sb.AppendLine($"**Total:** {edges.Count}");
        sb.AppendLine();
        foreach (var edge in edges)
            sb.AppendLine($"- `{edge.From.Value}` → `{edge.To.Value}` ({edge.Kind})");
        return sb.ToString();
    }

    private static string RenderDiffMarkdown(GraphDiff diff, string versionA, string versionB)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Diff: {versionA} → {versionB}");
        sb.AppendLine($"**Total changes:** {diff.Entries.Count}");
        sb.AppendLine();
        foreach (var entry in diff.Entries)
        {
            var marker = entry.ChangeKind switch
            {
                DiffChangeKind.Added => "+",
                DiffChangeKind.Removed => "-",
                _ => "~"
            };
            sb.AppendLine($"{marker} `{entry.Id.Value}`: {entry.Summary}");
        }
        return sb.ToString();
    }

    private static string RenderProjectMarkdown(Dictionary<string, object?> overview)
    {
        return JsonSerializer.Serialize(overview, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
