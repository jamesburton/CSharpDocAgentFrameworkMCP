using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocAgent.McpServer.Config;
using DocAgent.McpServer.Ingestion;
using DocAgent.McpServer.Security;
using DocAgent.McpServer.Telemetry;
using Microsoft.Build.Locator;
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
    private readonly AuditLogger _auditLogger;
    private readonly ILogger<IngestionTools> _logger;

    public IngestionTools(
        IIngestionService ingestionService,
        ISolutionIngestionService solutionIngestionService,
        TypeScriptIngestionService typeScriptIngestionService,
        PathAllowlist allowlist,
        AuditLogger auditLogger,
        ILogger<IngestionTools> logger)
    {
        _ingestionService = ingestionService;
        _solutionIngestionService = solutionIngestionService;
        _typeScriptIngestionService = typeScriptIngestionService;
        _allowlist = allowlist;
        _auditLogger = auditLogger;
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
        try
        {
            using var activity = DocAgentTelemetry.Source.StartActivity(
                "tool.ingest_project", ActivityKind.Internal);
            activity?.SetTag("tool.name", "ingest_project");

            if (string.IsNullOrWhiteSpace(path))
                return ErrorJson("invalid_input", "path is required.");

            var absolutePath = Path.GetFullPath(path);
            if (!_allowlist.IsAllowed(absolutePath))
            {
                _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorJson("access_denied", "Path is not in the configured allow list.");
            }

            // Extract progress token — may be null, string, or number.
            var progressToken = requestContext?.Params?.Meta?["progressToken"];

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
                                progress = (double)current,
                                total = (double)total,
                                message,
                            }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                    }
                }
                : null;

            var result = await _ingestionService.IngestAsync(
                absolutePath, include, exclude, forceReindex,
                progressCallback, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("tool.result.symbolCount", result.SymbolCount);
            activity?.SetTag("tool.result.projectCount", result.ProjectCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

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
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {Path}", path);
            return ErrorJson("ingestion_failed", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
        try
        {
            using var activity = DocAgentTelemetry.Source.StartActivity(
                "tool.ingest_solution", ActivityKind.Internal);
            activity?.SetTag("tool.name", "ingest_solution");

            if (string.IsNullOrWhiteSpace(path))
                return ErrorJson("invalid_input", "path is required.");

            var absolutePath = Path.GetFullPath(path);
            if (!_allowlist.IsAllowed(absolutePath))
            {
                _logger.LogWarning("Ingestion denied: path {Path} outside allowlist", absolutePath);
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorJson("access_denied", "Path is not in the configured allow list.");
            }

            // Extract progress token — may be null, string, or number.
            var progressToken = requestContext?.Params?.Meta?["progressToken"];

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
                                progress = (double)current,
                                total = (double)total,
                                message,
                            }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                    }
                }
                : null;

            var result = await _solutionIngestionService.IngestAsync(
                absolutePath, progressCallback, cancellationToken).ConfigureAwait(false);

            activity?.SetTag("tool.result.totalNodeCount", result.TotalNodeCount);
            activity?.SetTag("tool.result.ingestedProjectCount", result.IngestedProjectCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

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
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Solution ingestion failed for {Path}", path);
            return ErrorJson("ingestion_failed", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
        [Description("Force re-index even if snapshot is current")] bool forceReindex = false,
        CancellationToken cancellationToken = default)
    {
        try
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

            // Extract progress token — may be null, string, or number.
            var progressToken = requestContext?.Params?.Meta?["progressToken"];

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
                                progress = (double)current,
                                total = (double)total,
                                message,
                            }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                    }
                }
                : null;

            var result = await _typeScriptIngestionService.IngestTypeScriptAsync(
                absolutePath, cancellationToken, forceReindex, progressCallback).ConfigureAwait(false);

            activity?.SetTag("tool.result.symbolCount", result.SymbolCount);
            activity?.SetTag("tool.result.skipped", result.Skipped);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _auditLogger.Log(
                tool: "ingest_typescript",
                arguments: new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["symbolCount"] = result.SymbolCount,
                    ["skipped"] = result.Skipped,
                },
                result: null,
                duration: result.Duration,
                success: true);

            return JsonSerializer.Serialize(new
            {
                snapshotId = result.SnapshotId,
                symbolCount = result.SymbolCount,
                durationMs = result.Duration.TotalMilliseconds,
                warnings = result.Warnings,
                indexError = result.IndexError,
                skipped = result.Skipped,
                reason = result.Reason,
            }, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TypeScriptIngestionException ex) when (ex.Category is not null)
        {
            _logger.LogError(ex, "TypeScript ingestion failed for {Path} (category: {Category})", path, ex.Category);
            return ErrorJson(ex.Category, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypeScript ingestion failed for {Path}", path);
            return ErrorJson("ingestion_failed", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: ingest_typescript_workspace
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "ingest_typescript_workspace")]
    [Description("Ingest a TypeScript workspace (monorepo), discovering and ingesting all tsconfig.json projects under a root directory.")]
    public async Task<string> IngestTypeScriptWorkspace(
        ModelContextProtocol.Server.McpServer mcpServer,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Absolute path to the root directory")] string path,
        [Description("Force re-index even if snapshot is current")] bool forceReindex = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var activity = DocAgentTelemetry.Source.StartActivity(
                "tool.ingest_typescript_workspace", ActivityKind.Internal);
            activity?.SetTag("tool.name", "ingest_typescript_workspace");

            if (string.IsNullOrWhiteSpace(path))
                return ErrorJson("invalid_input", "path is required.");

            var absolutePath = Path.GetFullPath(path);
            if (!_allowlist.IsAllowed(absolutePath))
            {
                _logger.LogWarning("Workspace ingestion denied: path {Path} outside allowlist", absolutePath);
                activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
                return ErrorJson("access_denied", "Path is not in the configured allow list.");
            }

            // Extract progress token
            var progressToken = requestContext?.Params?.Meta?["progressToken"];

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
                                progress = (double)current,
                                total = (double)total,
                                message,
                            }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Progress notification failed (non-fatal)");
                    }
                }
                : null;

            var result = await _typeScriptIngestionService.IngestTypeScriptWorkspaceAsync(
                absolutePath, cancellationToken, forceReindex, progressCallback).ConfigureAwait(false);

            activity?.SetTag("tool.result.totalProjectCount", result.TotalProjectCount);
            activity?.SetTag("tool.result.ingestedCount", result.IngestedCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return JsonSerializer.Serialize(new
            {
                totalProjectCount = result.TotalProjectCount,
                ingestedCount = result.IngestedCount,
                durationMs = result.Duration.TotalMilliseconds,
                projects = result.Projects.Select(p => new
                {
                    tsconfigPath = p.TsconfigPath,
                    snapshotId = p.SnapshotId,
                    symbolCount = p.SymbolCount,
                    status = p.Status,
                    reason = p.Reason,
                }),
                warnings = result.Warnings,
            }, s_jsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TypeScriptIngestionException ex) when (ex.Category is not null)
        {
            _logger.LogError(ex, "TypeScript workspace ingestion failed for {Path} (category: {Category})", path, ex.Category);
            return ErrorJson(ex.Category, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TypeScript workspace ingestion failed for {Path}", path);
            return ErrorJson("ingestion_failed", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Tool: get_diagnostic_info
    // ─────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "get_diagnostic_info")]
    [Description("Get diagnostic information about the server environment, MSBuild registration, and Node.js availability.")]
    public Task<string> GetDiagnosticInfo(
        ModelContextProtocol.Server.McpServer mcpServer,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var msbuildInstances = MSBuildLocator.QueryVisualStudioInstances().Select(i => new
            {
                i.Name,
                i.Version,
                i.MSBuildPath,
                i.DiscoveryType
            }).ToList();

            var info = new
            {
                os = Environment.OSVersion.ToString(),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                isMSBuildRegistered = MSBuildLocator.IsRegistered,
                msbuildInstances = msbuildInstances,
                appBaseDir = AppContext.BaseDirectory,
                currentDir = Directory.GetCurrentDirectory(),
                envAllowedPaths = Environment.GetEnvironmentVariable("DOCAGENT_ALLOWED_PATHS"),
                serverVersion = "2.0.2"
            };

            return Task.FromResult(JsonSerializer.Serialize(info, s_jsonOptions));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorJson("diagnostic_failed", $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ErrorJson(string error, string? message) =>
        JsonSerializer.Serialize(new { error, message }, s_jsonOptions);
}
