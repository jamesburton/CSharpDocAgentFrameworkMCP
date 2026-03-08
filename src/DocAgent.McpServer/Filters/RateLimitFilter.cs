using System.Text.Json;
using DocAgent.McpServer.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DocAgent.McpServer.Filters;

/// <summary>
/// Extension methods for registering the rate limit filter in the MCP server builder.
/// The filter checks the <see cref="ToolRateLimiter"/> before forwarding tool calls
/// and returns a structured error response when the rate limit is exceeded.
/// </summary>
public static class RateLimitFilter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Registers the rate limit filter on the MCP server builder via WithRequestFilters.
    /// Must be registered BEFORE <see cref="AuditFilter.AddAuditFilter"/> so rate-limited
    /// requests are rejected before audit logging and tool execution.
    /// </summary>
    public static IMcpServerBuilder AddRateLimitFilter(this IMcpServerBuilder builder)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var limiter = context.Services!.GetRequiredService<ToolRateLimiter>();
                var toolName = context.Params?.Name ?? "unknown";

                var (allowed, lease, retryAfterMs) = limiter.TryAcquire(toolName);

                if (!allowed)
                {
                    return new CallToolResult
                    {
                        Content = [new TextContentBlock
                        {
                            Text = JsonSerializer.Serialize(new
                            {
                                error = "rate_limited",
                                message = $"Too many requests. Please retry after {retryAfterMs}ms.",
                                retryAfterMs,
                            }, s_jsonOptions),
                        }],
                        IsError = true,
                    };
                }

                try
                {
                    return await next(context, cancellationToken);
                }
                finally
                {
                    lease?.Dispose();
                }
            });
        });
    }
}
