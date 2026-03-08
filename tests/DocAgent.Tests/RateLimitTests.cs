using DocAgent.McpServer.Config;
using DocAgent.McpServer.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocAgent.Tests;

public sealed class RateLimitTests
{
    private static ToolRateLimiter CreateLimiter(
        bool enabled = true,
        int queryTokenLimit = 5,
        int queryTokensPerPeriod = 5,
        int ingestionTokenLimit = 2,
        int ingestionTokensPerPeriod = 2,
        int replenishmentSeconds = 60)
    {
        var opts = new DocAgentServerOptions
        {
            RateLimit = new RateLimitOptions
            {
                Enabled = enabled,
                QueryTokenLimit = queryTokenLimit,
                QueryTokensPerPeriod = queryTokensPerPeriod,
                QueryReplenishmentPeriodSeconds = replenishmentSeconds,
                IngestionTokenLimit = ingestionTokenLimit,
                IngestionTokensPerPeriod = ingestionTokensPerPeriod,
                IngestionReplenishmentPeriodSeconds = replenishmentSeconds,
            },
        };
        return new ToolRateLimiter(Options.Create(opts));
    }

    [Fact]
    public void TryAcquire_QueryWithinLimit_IsAllowed()
    {
        using var limiter = CreateLimiter(queryTokenLimit: 5);

        var (allowed, lease, _) = limiter.TryAcquire("search_symbols");

        allowed.Should().BeTrue();
        lease?.Dispose();
    }

    [Fact]
    public void TryAcquire_IngestionWithinLimit_IsAllowed()
    {
        using var limiter = CreateLimiter(ingestionTokenLimit: 2);

        var (allowed, lease, _) = limiter.TryAcquire("ingest_project");

        allowed.Should().BeTrue();
        lease?.Dispose();
    }

    [Fact]
    public void TryAcquire_QueryExceedsLimit_IsRejected()
    {
        using var limiter = CreateLimiter(queryTokenLimit: 2, queryTokensPerPeriod: 2);

        // Exhaust the bucket
        for (int i = 0; i < 2; i++)
        {
            var (ok, lease, _) = limiter.TryAcquire("search_symbols");
            ok.Should().BeTrue();
            lease?.Dispose();
        }

        // Third call should be rejected
        var (allowed, _, retryAfterMs) = limiter.TryAcquire("search_symbols");

        allowed.Should().BeFalse();
        retryAfterMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryAcquire_IngestionExceedsLimit_IsRejected()
    {
        using var limiter = CreateLimiter(ingestionTokenLimit: 1, ingestionTokensPerPeriod: 1);

        // Exhaust the bucket
        var (ok, lease, _) = limiter.TryAcquire("ingest_project");
        ok.Should().BeTrue();
        lease?.Dispose();

        // Second call should be rejected
        var (allowed, _, retryAfterMs) = limiter.TryAcquire("ingest_project");

        allowed.Should().BeFalse();
        retryAfterMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryAcquire_ExhaustQueryBucket_IngestionStillAllowed()
    {
        using var limiter = CreateLimiter(queryTokenLimit: 1, ingestionTokenLimit: 2);

        // Exhaust query bucket
        var (qOk, qLease, _) = limiter.TryAcquire("search_symbols");
        qOk.Should().BeTrue();
        qLease?.Dispose();

        var (qBlocked, _, _) = limiter.TryAcquire("search_symbols");
        qBlocked.Should().BeFalse("query bucket is exhausted");

        // Ingestion should still work
        var (iOk, iLease, _) = limiter.TryAcquire("ingest_project");
        iOk.Should().BeTrue("ingestion uses a separate bucket");
        iLease?.Dispose();
    }

    [Fact]
    public void TryAcquire_ExhaustIngestionBucket_QueryStillAllowed()
    {
        using var limiter = CreateLimiter(queryTokenLimit: 5, ingestionTokenLimit: 1);

        // Exhaust ingestion bucket
        var (iOk, iLease, _) = limiter.TryAcquire("ingest_solution");
        iOk.Should().BeTrue();
        iLease?.Dispose();

        var (iBlocked, _, _) = limiter.TryAcquire("ingest_solution");
        iBlocked.Should().BeFalse("ingestion bucket is exhausted");

        // Query should still work
        var (qOk, qLease, _) = limiter.TryAcquire("get_symbol");
        qOk.Should().BeTrue("query uses a separate bucket");
        qLease?.Dispose();
    }

    [Fact]
    public void TryAcquire_Disabled_AlwaysAllowed()
    {
        using var limiter = CreateLimiter(enabled: false);

        for (int i = 0; i < 100; i++)
        {
            var (allowed, lease, _) = limiter.TryAcquire("search_symbols");
            allowed.Should().BeTrue();
            lease?.Dispose();
        }
    }

    [Fact]
    public void TryAcquire_ToolCategorization_Correct()
    {
        using var limiter = CreateLimiter(queryTokenLimit: 1, ingestionTokenLimit: 1);

        // Exhaust both buckets
        var (qOk, qLease, _) = limiter.TryAcquire("search_symbols");
        qOk.Should().BeTrue();
        qLease?.Dispose();

        var (iOk, iLease, _) = limiter.TryAcquire("ingest_project");
        iOk.Should().BeTrue();
        iLease?.Dispose();

        // Query tools should be rejected
        limiter.TryAcquire("search_symbols").Allowed.Should().BeFalse();
        limiter.TryAcquire("get_symbol").Allowed.Should().BeFalse();
        limiter.TryAcquire("get_references").Allowed.Should().BeFalse();

        // Ingestion tools should be rejected
        limiter.TryAcquire("ingest_project").Allowed.Should().BeFalse();
        limiter.TryAcquire("ingest_solution").Allowed.Should().BeFalse();
    }
}
