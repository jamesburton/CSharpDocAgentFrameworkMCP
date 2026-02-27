using System.ComponentModel;
using System.Text.Json;
using DocAgent.Core;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Serialization;
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

    private readonly IKnowledgeQueryService _query;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<DocTools> _logger;
    private readonly DocAgentServerOptions _options;

    public DocTools(
        IKnowledgeQueryService query,
        PathAllowlist allowlist,
        ILogger<DocTools> logger,
        IOptions<DocAgentServerOptions> options)
    {
        _query = query;
        _allowlist = allowlist;
        _logger = logger;
        _options = options.Value;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool 1: search_symbols  (MCPS-01)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "search_symbols"), Description("Search symbols and documentation by keyword.")]
    public async Task<string> SearchSymbols(
        [Description("Search query")] string query,
        [Description("Symbol kind filter (optional): Namespace, Type, Method, Property, Field, Event, Parameter, Constructor, Delegate, Indexer, Operator, Destructor, EnumMember, TypeParameter")] string? kindFilter = null,
        [Description("Result offset for pagination")] int offset = 0,
        [Description("Result limit (max 100)")] int limit = 20,
        [Description("Include full doc comments in results")] bool fullDocs = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
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
            ct: cancellationToken);

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
                    : sanitizedItems.Select(i => new { id = i.Id.Value, score = i.Score, kind = i.Kind.ToString(), displayName = i.DisplayName, snippet = i.Snippet }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeSearchResults(sanitizedItems),
        () => RenderSearchMarkdown(sanitizedItems, envelope));
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
        if (string.IsNullOrWhiteSpace(symbolId))
            return ErrorResponse(QueryErrorKind.InvalidInput, "symbolId is required.");

        var id = new SymbolId(symbolId);
        var result = await _query.GetSymbolAsync(id, ct: cancellationToken);

        if (!result.Success)
            return ErrorResponse(result.Error!.Value, result.ErrorMessage);

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

    // ─────────────────────────────────────────────────────────────────
    // Tool 3: get_references  (MCPS-03)
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_references"), Description("Get symbols that reference the given symbol.")]
    public async Task<string> GetReferences(
        [Description("Stable SymbolId")] string symbolId,
        [Description("Include surrounding code context (not yet implemented)")] bool includeContext = false,
        [Description("Output format: json|markdown|tron")] string format = "json",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
            return ErrorResponse(QueryErrorKind.InvalidInput, "symbolId is required.");

        var id = new SymbolId(symbolId);

        // Buffer the async enumerable into a list
        var edges = new List<SymbolEdge>();
        try
        {
            await foreach (var edge in _query.GetReferencesAsync(id, cancellationToken))
                edges.Add(edge);
        }
        catch (SymbolNotFoundException)
        {
            return ErrorResponse(QueryErrorKind.NotFound, $"Symbol '{symbolId}' not found.");
        }

        bool hasInjectionWarning = false; // edges have no doc content

        return FormatResponse(format, () =>
        {
            var payload = new
            {
                promptInjectionWarning = hasInjectionWarning,
                total = edges.Count,
                references = edges.Select(e => new
                {
                    fromId = e.From.Value,
                    toId = e.To.Value,
                    edgeKind = e.Kind.ToString(),
                }).ToList()
            };
            return JsonSerializer.Serialize(payload, s_jsonOptions);
        },
        () => TronSerializer.SerializeReferences(edges),
        () => RenderReferencesMarkdown(edges));
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
        if (string.IsNullOrWhiteSpace(versionA) || string.IsNullOrWhiteSpace(versionB))
            return ErrorResponse(QueryErrorKind.InvalidInput, "versionA and versionB are required.");

        var a = new SnapshotRef(versionA);
        var b = new SnapshotRef(versionB);

        var result = await _query.DiffAsync(a, b, cancellationToken);

        if (!result.Success)
            return ErrorResponse(result.Error!.Value, result.ErrorMessage);

        var envelope = result.Value!;
        var diff = envelope.Payload;

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

        return FormatResponse(format, () =>
            JsonSerializer.Serialize(overview, s_jsonOptions),
        () => TronSerializer.SerializeProjectOverview(overview),
        () => RenderProjectMarkdown(overview));
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

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
