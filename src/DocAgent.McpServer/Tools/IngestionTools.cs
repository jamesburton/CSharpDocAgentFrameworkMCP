using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Tools;

/// <summary>
/// MCP tool handler for runtime ingestion of .NET and TypeScript projects.
/// Validates path against the PathAllowlist before delegating to ingestion services.
/// </summary>
[McpServerToolType]
public sealed class IngestionTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IIngestionService _ingestionService;
    private readonly ISolutionIngestionService _solutionIngestionService;
    private readonly TypeScriptIngestionService _typeScriptIngestionService;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<IngestionTools> _logger;

    public IngestionTools(
        IIngestionService ingestionService,
        ISolutionIngestionService solutionIngestionService,
        TypeScriptIngestionService typeScriptIngestionService,
        PathAllowlist allowlist,
        ILogger<IngestionTools> logger)
    {
        _ingestionService = ingestionService;
        _solutionIngestionService = solutionIngestionService;
        _typeScriptIngestionService = typeScriptIngestionService;
        _allowlist = allowlist;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: ingest_project
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ingest_project")]
    [Description("Ingest a .NET project or solution, building a queryable symbol graph.")]
    public async Task<string> IngestProject(
        ModelContextProtocol.Server.McpServer mcpServer,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Absolute path to .sln, .slnx, or .csproj file")] string path,
        [Description("Glob pattern to include projects (e.g. **/*.csproj)")] string? include = null,
        [Description("Glob pattern to exclude projects (e.g. **/Tests/**)")] string? exclude = null,
        [Description("Force re-index even if snapshot is current")] bool forceReindex = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.ingest_project", ActivityKind.Internal);
        activity?.SetTag("tool.name", "ingest_project");

        // 1. Validate path is not null/empty.
        if (string.IsNullOrWhiteSpace(path))
            return ErrorJson("invalid_input", "path is required.");

        // 2. PathAllowlist check — fail fast before any pipeline work.
        var absolutePath = Path.GetFullPath(path);
        if (!_allowlist.IsAllowed(absolutePath))
        {
            _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
            return ErrorJson("access_denied", "Path is not in the configured allow list.");
        }

        // 3. Extract progress token — may be null if client did not supply one.
        var progressTokenNode = requestContext?.Params?.Meta?["progressToken"];
        var progressToken = progressTokenNode?.GetValue<string>();

        // 4. Build progress callback — only invoked when progressToken is non-null (MCP spec requirement).
        Func<int, int, string, Task>? progressCallback = progressToken is not null
            ? async (current, total, message) =>
            {
                try
                {
                    await mcpServer.SendNotificationAsync(
                        "notifications/progress",
                        new
                        {
                            progressToken,
                            progress = current,
                            total,
                            message,
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                }
            }
            : null;

        try
        {
            // 5. Delegate to the orchestrating service.
            var result = await _ingestionService.IngestAsync(
                absolutePath, include, exclude, forceReindex,
                progressCallback, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("tool.result.symbolCount", result.SymbolCount);
            activity?.SetTag("tool.result.projectCount", result.ProjectCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // 6. Return success JSON.
            return JsonSerializer.Serialize(new
            {
                snapshotId = result.SnapshotId,
                symbolCount = result.SymbolCount,
                projectCount = result.ProjectCount,
                durationMs = result.Duration.TotalMilliseconds,
                warnings = result.Warnings,
                indexError = result.IndexError,
            }, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {Path}", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ErrorJson("ingestion_failed", ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: ingest_solution
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ingest_solution")]
    [Description("Ingest an entire .NET solution (.sln), building a queryable symbol graph across all C# projects.")]
    public async Task<string> IngestSolution(
        ModelContextProtocol.Server.McpServer mcpServer,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Absolute path to .sln file")] string path,
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.ingest_solution", ActivityKind.Internal);
        activity?.SetTag("tool.name", "ingest_solution");

        // 1. Validate path is not null/empty.
        if (string.IsNullOrWhiteSpace(path))
            return ErrorJson("invalid_input", "path is required.");

        // 2. PathAllowlist check — fail fast before any pipeline work.
        var absolutePath = Path.GetFullPath(path);
        if (!_allowlist.IsAllowed(absolutePath))
        {
            _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
            return ErrorJson("access_denied", "Path is not in the configured allow list.");
        }

        // 3. Extract progress token — may be null if client did not supply one.
        var progressTokenNode = requestContext?.Params?.Meta?["progressToken"];
        var progressToken = progressTokenNode?.GetValue<string>();

        // 4. Build progress callback — only invoked when progressToken is non-null (MCP spec requirement).
        Func<int, int, string, Task>? progressCallback = progressToken is not null
            ? async (current, total, message) =>
            {
                try
                {
                    await mcpServer.SendNotificationAsync(
                        "notifications/progress",
                        new
                        {
                            progressToken,
                            progress = current,
                            total,
                            message,
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                }
            }
            : null;

        try
        {
            // 5. Delegate to the solution ingestion service.
            var result = await _solutionIngestionService.IngestAsync(
                absolutePath, progressCallback, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("tool.result.totalNodeCount", result.TotalNodeCount);
            activity?.SetTag("tool.result.ingestedProjectCount", result.IngestedProjectCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // 6. Return success JSON.
            return JsonSerializer.Serialize(new
            {
                snapshotId = result.SnapshotId,
                solutionName = result.SolutionName,
                totalProjectCount = result.TotalProjectCount,
                ingestedProjectCount = result.IngestedProjectCount,
                totalNodeCount = result.TotalNodeCount,
                totalEdgeCount = result.TotalEdgeCount,
                durationMs = result.Duration.TotalMilliseconds,
                projects = result.Projects.Select(p => new
                {
                    name = p.Name,
                    filePath = p.FilePath,
                    status = p.Status,
                    reason = p.Reason,
                    nodeCount = p.NodeCount,
                    chosenTfm = p.ChosenTfm,
                }),
                warnings = result.Warnings,
            }, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Solution ingestion failed for {Path}", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ErrorJson("ingestion_failed", ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: ingest_typescript
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ingest_typescript")]
    [Description("Ingest a TypeScript project (tsconfig.json), building a queryable symbol graph.")]
    public async Task<string> IngestTypeScript(
        ModelContextProtocol.Server.McpServer mcpServer,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Absolute path to tsconfig.json file")] string path,
        CancellationToken cancellationToken = default)
    {
        using var activity = DocAgentTelemetry.Source.StartActivity(
            "tool.ingest_typescript", ActivityKind.Internal);
        activity?.SetTag("tool.name", "ingest_typescript");

        if (string.IsNullOrWhiteSpace(path))
            return ErrorJson("invalid_input", "path is required.");

        var absolutePath = Path.GetFullPath(path);
        if (!_allowlist.IsAllowed(absolutePath))
        {
            _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
            return ErrorJson("access_denied", "Path is not in the configured allow list.");
        }

        try
        {
            var result = await _typeScriptIngestionService.IngestTypeScriptAsync(
                absolutePath, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("tool.result.symbolCount", result.SymbolCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return JsonSerializer.Serialize(new
            {
                snapshotId = result.SnapshotId,
                symbolCount = result.SymbolCount,
                durationMs = result.Duration.TotalMilliseconds,
                warnings = result.Warnings,
                indexError = result.IndexError,
            }, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypeScript ingestion failed for {Path}", absolutePath);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return ErrorJson("ingestion_failed", ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ErrorJson(string error, string? message) =>
        JsonSerializer.Serialize(new { error, message }, s_jsonOptions);
}
