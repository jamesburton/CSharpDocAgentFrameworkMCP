---
phase: 05-mcp-server-security
plan: 02
subsystem: tests
tags: [testing, security, mcp, path-allowlist, audit-logger, prompt-injection, unit-tests, xunit, fluentassertions]

requires:
  - phase: 05-mcp-server-security
    plan: 01
    provides: PathAllowlist, AuditLogger, PromptInjectionScanner, DocTools (5 MCP tool handlers)

provides:
  - PathAllowlistTests: 8 tests (default-deny, glob matching, deny-precedence, traversal, env-var, case-insensitive, deny-on-cwd)
  - AuditLoggerTests: 6 tests (success, error, default verbosity, verbose mode, file output, redaction, null tool)
  - PromptInjectionScannerTests: 10 tests (clean, 6 injection patterns, case-insensitive, null, partial match)
  - McpToolTests: 19 tests (all 5 tool handlers with StubKnowledgeQueryService)

affects: [05-03-integration-tests]

tech-stack:
  added: []
  patterns:
    - "Manual stub IKnowledgeQueryService (no Moq) with IAsyncEnumerable via async-iterator"
    - "CaptureLogger<T> implementing ILogger<T> for intercepting AuditLogger output"
    - "Options.Create(new DocAgentServerOptions { ... }) for direct PathAllowlist/AuditLogger instantiation"
    - "JsonDocument.Parse + FluentAssertions for structured JSON assertion"

key-files:
  created:
    - tests/DocAgent.Tests/PathAllowlistTests.cs
    - tests/DocAgent.Tests/AuditLoggerTests.cs
    - tests/DocAgent.Tests/PromptInjectionScannerTests.cs
    - tests/DocAgent.Tests/McpToolTests.cs
  modified:
    - tests/DocAgent.Tests/DocAgent.Tests.csproj
    - src/DocAgent.McpServer/Security/PathAllowlist.cs

key-decisions:
  - "PathAllowlist.MatchesAny fixed: FileSystemGlobbing Matcher.Match(string) returns false for absolute paths — must strip path root and use Match(root, relativePath)"
  - "StubKnowledgeQueryService uses async-iterator method for GetReferencesAsync (IAsyncEnumerable) — [EnumeratorCancellation] attribute on implementation, not interface"
  - "AuditLogger file-append is fire-and-forget — test uses Task.Delay(200ms) to allow async write before asserting"
  - "permissiveAllowlist uses AllowedPaths = [\"**\"] — after the PathAllowlist bug fix this correctly allows all paths"

duration: 23min
completed: 2026-02-27
---

# Phase 05 Plan 02: Security Unit Tests Summary

**Comprehensive unit tests for PathAllowlist, AuditLogger, PromptInjectionScanner, and all five MCP tool handlers with manual stub — 45 new tests, 131 total passing**

## Performance

- **Duration:** 23 min
- **Started:** 2026-02-27T04:35:53Z
- **Completed:** 2026-02-27T04:59:00Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- Task 1: 3 security service test files — PathAllowlistTests (8), AuditLoggerTests (6), PromptInjectionScannerTests (10) = 24 tests
- Task 2: McpToolTests (19 tests) covering all 5 tools with StubKnowledgeQueryService + StaleIndexStub + InjectionDocStub
- Fixed PathAllowlist glob-matching bug discovered by tests (Matcher requires relative paths)
- Added McpServer project reference to test project csproj
- Total test suite: 131 tests, 0 failures

## Task Commits

1. **Task 1: Security service unit tests** - `6e06565` (feat)
2. **Task 2: MCP tool handler unit tests** - `01b2842` (feat)

## Files Created/Modified

- `tests/DocAgent.Tests/DocAgent.Tests.csproj` - Added McpServer project reference
- `tests/DocAgent.Tests/PathAllowlistTests.cs` - 8 tests: default-deny, glob match, deny-precedence, traversal normalization, env-var, case-insensitive, deny-on-cwd
- `tests/DocAgent.Tests/AuditLoggerTests.cs` - 6 tests: success, error, verbosity tiers, file output, redaction, null-tool fallback; uses CaptureLogger<T>
- `tests/DocAgent.Tests/PromptInjectionScannerTests.cs` - 10 tests: clean content, 6 known patterns (ignore/you-are-now/system-prompt/forget/act-as/disregard), case-insensitive, null, partial match
- `tests/DocAgent.Tests/McpToolTests.cs` - 19 tests: all 5 tools, 3 stubs (default/stale-index/injection-doc), JSON assertion via JsonDocument.Parse
- `src/DocAgent.McpServer/Security/PathAllowlist.cs` - Fixed MatchesAny to use Match(root, relativePath) for correct absolute-path glob matching

## Decisions Made

- `FileSystemGlobbing.Matcher.Match(absolutePath)` returns `false` for absolute paths — fix: strip path root, use `Match(root, relativePath)` overload
- Manual stub pattern (no Moq): `StubKnowledgeQueryService`, `StaleIndexStub`, `InjectionDocStub` inner classes in test file
- `[EnumeratorCancellation]` on IAsyncEnumerable implementation (not interface) — pre-existing decision from Phase 1

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed PathAllowlist.MatchesAny absolute-path glob matching**
- **Found during:** Task 1 (running PathAllowlistTests)
- **Issue:** `Matcher.Match(absolutePath)` with an absolute glob pattern always returns `false`. `FileSystemGlobbing` requires relative paths — the `Match(string file)` overload doesn't resolve drive letters or absolute roots
- **Fix:** Rewrote `MatchesAny` to strip the path root from both the file and each pattern, then call `matcher.Match(root, relativePath)` overload
- **Files modified:** `src/DocAgent.McpServer/Security/PathAllowlist.cs`
- **Commit:** `6e06565` (included in Task 1 commit)
- **Verification:** All 8 PathAllowlistTests pass; full test suite 131/131 pass

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Necessary fix — without it, PathAllowlist silently denied all glob-matched paths. Security boundary was broken.

## Self-Check

- [x] `tests/DocAgent.Tests/PathAllowlistTests.cs` — exists
- [x] `tests/DocAgent.Tests/AuditLoggerTests.cs` — exists
- [x] `tests/DocAgent.Tests/PromptInjectionScannerTests.cs` — exists
- [x] `tests/DocAgent.Tests/McpToolTests.cs` — exists
- [x] Task 1 commit `6e06565` — exists
- [x] Task 2 commit `01b2842` — exists
- [x] `dotnet test` result: 131 passed, 0 failed

## Self-Check: PASSED
