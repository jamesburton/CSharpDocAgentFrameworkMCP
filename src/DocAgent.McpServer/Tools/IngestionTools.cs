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
/// MCP tool handler for runtime ingestion of .NET projects and solutions.
/// Validates path against the PathAllowlist before delegating to <see cref="IIngestionService"/>.
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
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<IngestionTools> _logger;

    public IngestionTools(
        IIngestionService ingestionService,
        PathAllowlist allowlist,
        ILogger<IngestionTools> logger)
    {
        _ingestionService = ingestionService;
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
        // Meta is a JsonObject; progressToken is stored under the "progressToken" key.
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ErrorJson(string error, string? message) =>
        JsonSerializer.Serialize(new { error, message }, s_jsonOptions);
}
