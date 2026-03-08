---
phase: 25-server-infrastructure
plan: 02
subsystem: serving
tags: [rate-limiting, token-bucket, security, throttling, mcp-filter]

# Dependency graph
requires: [23-01, 25-01]
provides:
  - "ToolRateLimiter DI singleton with separate query/ingestion TokenBucketRateLimiters"
  - "RateLimitFilter MCP filter returning structured JSON error with retryAfterMs"
  - "RateLimitOptions configuration model with per-bucket tuning"
affects: [26-api-extensions]

# Tech tracking
tech-stack:
  added:
    - "System.Threading.RateLimiting.TokenBucketRateLimiter (BCL)"
  patterns:
    - "Token-bucket rate limiting with separate category buckets"
    - "MCP filter chain ordering (rate limit before audit)"
    - "Structured error response with retry guidance"

key-files:
  created:
    - "src/DocAgent.McpServer/RateLimiting/ToolRateLimiter.cs"
    - "src/DocAgent.McpServer/Filters/RateLimitFilter.cs"
    - "tests/DocAgent.Tests/RateLimitTests.cs"
  modified:
    - "src/DocAgent.McpServer/Config/DocAgentServerOptions.cs"
    - "src/DocAgent.McpServer/Program.cs"

key-decisions:
  - "Separate query and ingestion buckets so heavy querying cannot block ingestion"
  - "RateLimitFilter registered before AuditFilter for early rejection (less work wasted)"
  - "Rate limiting disabled by default via RateLimit.Enabled=true with generous defaults (100 query, 10 ingestion per 60s)"
  - "Structured JSON error includes retryAfterMs for client retry guidance"
  - "QueueLimit=0 means no queuing -- requests are immediately rejected or allowed"

patterns-established:
  - "MCP filter extension method pattern (AddRateLimitFilter matching AddAuditFilter)"
  - "DI singleton for shared rate limiter state across all tool calls"
  - "Internal static methods for testability (IsIngestionTool)"

requirements-completed: [OPS-03]

# Metrics
duration: 20min
completed: 2026-03-08
---

# Phase 25 Plan 02: Rate Limiting Summary

**Token-bucket rate limiting with separate query/ingestion buckets using BCL TokenBucketRateLimiter, MCP filter returning structured JSON error with retryAfterMs**

## Performance

- **Duration:** 20 min
- **Started:** 2026-03-08T03:02:00Z
- **Completed:** 2026-03-08T03:22:00Z
- **Tasks:** 2
- **Files created:** 3
- **Files modified:** 2

## Accomplishments
- Added RateLimitOptions configuration model with per-bucket tuning (token limit, tokens per period, replenishment period)
- Created ToolRateLimiter DI singleton wrapping two TokenBucketRateLimiters (query + ingestion)
- Created RateLimitFilter MCP filter following AuditFilter pattern, returning structured JSON error with retryAfterMs
- Filter registered before AuditFilter in pipeline for early rejection
- Ingestion tools (ingest_project, ingest_solution) use ingestion bucket; all others use query bucket
- Rate limiting configurable and disableable via RateLimit.Enabled
- 8 unit tests covering all rate limiting behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: Create RateLimitOptions, ToolRateLimiter singleton, and RateLimitFilter** - `98ed123` (feat)
2. **Task 2: Unit tests for ToolRateLimiter and rate limit behavior** - `86ae5b3` (test)

## Files Created/Modified
- `src/DocAgent.McpServer/Config/DocAgentServerOptions.cs` - Added RateLimitOptions class and RateLimit property
- `src/DocAgent.McpServer/RateLimiting/ToolRateLimiter.cs` - DI singleton with separate query/ingestion TokenBucketRateLimiters
- `src/DocAgent.McpServer/Filters/RateLimitFilter.cs` - MCP filter returning structured JSON error on rate limit exceeded
- `src/DocAgent.McpServer/Program.cs` - Registered ToolRateLimiter singleton and RateLimitFilter before AuditFilter
- `tests/DocAgent.Tests/RateLimitTests.cs` - 8 unit tests covering within-limit, exceeded-limit, separate buckets, disabled mode, tool categorization

## Decisions Made
- Separate query and ingestion buckets so heavy querying cannot block ingestion and vice versa
- RateLimitFilter registered before AuditFilter for early rejection (less computation wasted)
- Generous defaults (100 query tokens, 10 ingestion tokens per 60s) with Enabled=true
- Structured JSON error with retryAfterMs for client retry guidance
- QueueLimit=0 for immediate accept/reject (no queuing)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Stuck testhost processes from background test runs blocked build/test temporarily; resolved by using alternate output path

## User Setup Required
None - rate limiting is enabled by default with generous limits. Override via appsettings.json:
```json
{
  "DocAgent": {
    "RateLimit": {
      "Enabled": true,
      "QueryTokenLimit": 100,
      "IngestionTokenLimit": 10
    }
  }
}
```

## Next Phase Readiness
- Server infrastructure complete (startup validation + rate limiting)
- Ready for Phase 26 (API Extensions) which depends on Phase 25

---
*Phase: 25-server-infrastructure*
*Completed: 2026-03-08*
