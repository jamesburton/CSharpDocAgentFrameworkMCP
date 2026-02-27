using System.Diagnostics;
using System.Text.Json;
using DocAgent.McpServer.Security;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Filters;

/// <summary>
/// Extension methods for registering the audit filter in the MCP server builder.
/// The filter wraps every tool call with logging and structured error handling.
/// </summary>
public static class AuditFilter
{
    private static readonly JsonSerializerOptions s_errorJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Registers the audit filter on the MCP server builder via WithRequestFilters.
    /// Logs every tool call (input summary, duration, outcome) via AuditLogger.
    /// On unexpected exceptions, returns a structured error response instead of propagating.
    /// </summary>
    public static IMcpServerBuilder AddAuditFilter(this IMcpServerBuilder builder)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var audit = context.Services!.GetRequiredService<AuditLogger>();
                var sw = Stopwatch.StartNew();

                CallToolResult result;
                try
                {
                    result = await next(context, cancellationToken);
                    sw.Stop();
                    audit.Log(
                        tool: context.Params?.Name,
                        arguments: context.Params?.Arguments?.ToDictionary(
                            kv => kv.Key,
                            kv => (object?)kv.Value.ToString()),
                        result: result,
                        duration: sw.Elapsed,
                        success: true);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    var errorJson = BuildErrorJson(ex);
                    audit.Log(
                        tool: context.Params?.Name,
                        arguments: context.Params?.Arguments?.ToDictionary(
                            kv => kv.Key,
                            kv => (object?)kv.Value.ToString()),
                        result: null,
                        duration: sw.Elapsed,
                        success: false,
                        error: ex.GetType().Name);

                    result = new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = errorJson }],
                        IsError = true,
                    };
                }

                return result;
            });
        });
    }

    private static string BuildErrorJson(Exception ex)
    {
        return JsonSerializer.Serialize(new
        {
            error = "internal_error",
            message = "An unexpected error occurred processing the tool request.",
            errorType = ex.GetType().Name,
        }, s_errorJsonOptions);
    }
}
