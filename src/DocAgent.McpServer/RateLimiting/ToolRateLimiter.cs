using System.Threading.RateLimiting;
using DocAgent.McpServer.Config;
using Microsoft.Extensions.Options;

namespace DocAgent.McpServer.RateLimiting;

/// <summary>
/// DI singleton that wraps two <see cref="TokenBucketRateLimiter"/> instances --
/// one for query tools and one for ingestion tools. Each tool call acquires a
/// permit from the appropriate bucket before proceeding.
/// </summary>
public sealed class ToolRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter? _queryLimiter;
    private readonly TokenBucketRateLimiter? _ingestionLimiter;
    private readonly bool _enabled;

    public ToolRateLimiter(IOptions<DocAgentServerOptions> options)
    {
        var rl = options.Value.RateLimit;
        _enabled = rl.Enabled;

        if (!_enabled)
            return;

        _queryLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = rl.QueryTokenLimit,
            TokensPerPeriod = rl.QueryTokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromSeconds(rl.QueryReplenishmentPeriodSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

        _ingestionLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = rl.IngestionTokenLimit,
            TokensPerPeriod = rl.IngestionTokensPerPeriod,
            ReplenishmentPeriod = TimeSpan.FromSeconds(rl.IngestionReplenishmentPeriodSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>
    /// Attempts to acquire a rate limit lease for the given tool name.
    /// </summary>
    /// <returns>
    /// A tuple of (Allowed, Lease, RetryAfterMs). When <c>Allowed</c> is false the caller
    /// should return a structured error with the suggested retry delay.
    /// </returns>
    public (bool Allowed, RateLimitLease? Lease, int RetryAfterMs) TryAcquire(string toolName)
    {
        if (!_enabled)
            return (true, null, 0);

        var limiter = IsIngestionTool(toolName) ? _ingestionLimiter! : _queryLimiter!;
        var lease = limiter.AttemptAcquire(permitCount: 1);

        if (lease.IsAcquired)
            return (true, lease, 0);

        // Extract retry-after metadata from the lease, default to 1000ms
        var retryAfterMs = 1000;
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            retryAfterMs = (int)Math.Ceiling(retryAfter.TotalMilliseconds);
        }

        lease.Dispose();
        return (false, null, retryAfterMs);
    }

    /// <summary>
    /// Returns true for ingestion-category tools, false for query-category tools.
    /// </summary>
    internal static bool IsIngestionTool(string toolName) =>
        toolName is "ingest_project" or "ingest_solution";

    public void Dispose()
    {
        _queryLimiter?.Dispose();
        _ingestionLimiter?.Dispose();
    }
}
