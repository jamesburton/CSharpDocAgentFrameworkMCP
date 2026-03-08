---
phase: 25-server-infrastructure
plan: 01
subsystem: serving
tags: [startup-validation, fail-fast, configuration, hosted-service]

# Dependency graph
requires: [23-01]
provides:
  - "StartupValidator IHostedLifecycleService with pure static Validate method"
  - "Fail-fast on invalid ArtifactsDir or missing AllowedPaths configuration"
affects: [25-02, 26-api-extensions]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "IHostedLifecycleService.StartingAsync for pre-transport validation"
    - "Pure static Validate() method for unit-testable configuration validation"
    - "Environment.ExitCode + StopApplication() for graceful non-zero exit"

key-files:
  created:
    - "src/DocAgent.McpServer/Validation/StartupValidator.cs"
    - "tests/DocAgent.Tests/StartupValidatorTests.cs"
  modified:
    - "src/DocAgent.McpServer/Program.cs"

key-decisions:
  - "AllowedPaths empty is a warning (not error) because PathAllowlist defaults to cwd safely"
  - "Used IHostedLifecycleService.StartingAsync (runs before StartAsync) for earliest possible validation"
  - "Set Environment.ExitCode=1 before StopApplication() to guarantee non-zero exit"
  - "Catch both IOException and UnauthorizedAccessException for ArtifactsDir writability probe"

patterns-established:
  - "Validation via pure static method + IHostedLifecycleService wrapper for testability"
  - "All diagnostics via ILogger (routed to stderr by existing console config)"

requirements-completed: [OPS-02]

# Metrics
duration: 15min
completed: 2026-03-08
---

# Phase 25 Plan 01: Startup Validation Summary

**IHostedLifecycleService startup validator with pure static Validate(DocAgentServerOptions) method, checking AllowedPaths config and ArtifactsDir writability before MCP transport accepts connections**

## Performance

- **Duration:** 15 min
- **Started:** 2026-03-08T02:47:13Z
- **Completed:** 2026-03-08T03:02:00Z
- **Tasks:** 2
- **Files created:** 2
- **Files modified:** 1

## Accomplishments
- Created StartupValidator with pure static Validate() method returning ValidationResult (errors + warnings)
- Validation checks: AllowedPaths empty (warning), ArtifactsDir null/empty (error), ArtifactsDir not writable (error)
- IHostedLifecycleService.StartingAsync runs before any hosted service starts, ensuring MCP transport never accepts invalid config
- Calls Environment.ExitCode=1 + StopApplication() for graceful shutdown with non-zero exit
- Registered as hosted service in Program.cs
- 6 unit tests covering all validation paths, callable without host/DI

## Task Commits

Each task was committed atomically:

1. **Task 1: Create StartupValidator with static Validate method and IHostedLifecycleService wrapper** - `f876695` (feat)
2. **Task 2: Unit tests for StartupValidator.Validate()** - `08d73e5` (test)

## Files Created/Modified
- `src/DocAgent.McpServer/Validation/StartupValidator.cs` - ValidationResult record + StartupValidator class with pure static Validate() and IHostedLifecycleService wrapper
- `tests/DocAgent.Tests/StartupValidatorTests.cs` - 6 unit tests covering valid config, empty AllowedPaths warning, null/empty ArtifactsDir error, non-writable ArtifactsDir error, env var override
- `src/DocAgent.McpServer/Program.cs` - Added StartupValidator hosted service registration and using directive

## Decisions Made
- AllowedPaths empty is a warning (not error) because PathAllowlist has a safe default (cwd only)
- Used IHostedLifecycleService.StartingAsync for earliest possible validation hook
- Set Environment.ExitCode=1 before StopApplication() to guarantee non-zero exit code
- Catch both IOException and UnauthorizedAccessException for ArtifactsDir writability probe

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Added UnauthorizedAccessException catch for ArtifactsDir probe**
- **Found during:** Task 1
- **Issue:** Plan only mentioned IOException for ArtifactsDir writability probe, but UnauthorizedAccessException is equally likely on permission-denied paths
- **Fix:** Added catch for UnauthorizedAccessException alongside IOException
- **Files modified:** src/DocAgent.McpServer/Validation/StartupValidator.cs
- **Commit:** f876695

**Total deviations:** 1 auto-fixed (missing critical functionality)
**Impact on plan:** Minor enhancement to error handling completeness. No scope creep.

## Issues Encountered
- Bitdefender file locks intermittently blocked builds (pre-existing, unrelated to changes)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- StartupValidator in place for Phase 25-02 (rate limiting) which also modifies Program.cs
- Validation runs before MCP transport, ensuring clean startup for all subsequent server features

---
*Phase: 25-server-infrastructure*
*Completed: 2026-03-08*
