---
phase: 06-analysis-hosting
plan: 01
subsystem: analysis
tags: [roslyn, diagnostic-analyzer, netstandard2.0, code-quality]

requires:
  - phase: none
    provides: standalone analyzer project (no cross-project dependencies)
provides:
  - DocAgent.Analyzers project with DOCAGENT001, DOCAGENT002, DOCAGENT003 analyzers
  - ExcludeFromDocCoverageAttribute for suppression
  - Configurable doc coverage threshold via MSBuild property
affects: [06-analysis-hosting, mcp-server-integration]

tech-stack:
  added: [Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit 1.1.2]
  patterns: [DiagnosticAnalyzer with RegisterSymbolAction, CompilationStart/End for aggregation, CSharpAnalyzerTest verifier pattern]

key-files:
  created:
    - src/DocAgent.Analyzers/DocAgent.Analyzers.csproj
    - src/DocAgent.Analyzers/ExcludeFromDocCoverageAttribute.cs
    - src/DocAgent.Analyzers/DocParity/DocParityAnalyzer.cs
    - src/DocAgent.Analyzers/SuspiciousEdit/SuspiciousEditAnalyzer.cs
    - src/DocAgent.Analyzers/Coverage/DocCoverageAnalyzer.cs
    - tests/DocAgent.Tests/Analyzers/DocParityAnalyzerTests.cs
    - tests/DocAgent.Tests/Analyzers/SuspiciousEditAnalyzerTests.cs
    - tests/DocAgent.Tests/Analyzers/DocCoverageAnalyzerTests.cs
  modified:
    - Directory.Packages.props
    - src/DocAgentFramework.sln
    - tests/DocAgent.Tests/DocAgent.Tests.csproj

key-decisions:
  - "RS2008 and RS1032 suppressed via NoWarn — release tracking and message formatting rules not needed for internal analyzers"
  - "CompilationEnd custom tag required by EnforceExtendedAnalyzerRules for DocCoverageAnalyzer descriptor"

patterns-established:
  - "Analyzer testing: CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> with inline source and DiagnosticResult expectations"
  - "Coverage threshold: configurable via build_property.DocCoverageThreshold in .globalconfig or MSBuild"

requirements-completed: [ANLY-01, ANLY-02, ANLY-03]

duration: 21min
completed: 2026-02-27
---

# Phase 06 Plan 01: Roslyn Analyzers Summary

**Three Roslyn DiagnosticAnalyzers (DOCAGENT001-003) for doc parity, suspicious edit detection, and coverage enforcement targeting netstandard2.0**

## Performance

- **Duration:** 21 min
- **Started:** 2026-02-27T19:20:42Z
- **Completed:** 2026-02-27T19:41:52Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- DocParityAnalyzer (DOCAGENT001) flags undocumented public symbols with ExcludeFromDocCoverage suppression
- SuspiciousEditAnalyzer (DOCAGENT002) detects [Obsolete] without doc mention and nullability attribute mismatches
- DocCoverageAnalyzer (DOCAGENT003) enforces configurable coverage threshold (default 80%) via CompilationEnd aggregation
- 10 analyzer tests passing alongside 157 total tests

## Task Commits

Each task was committed atomically:

1. **Task 1: Create DocAgent.Analyzers project with DocParityAnalyzer and SuspiciousEditAnalyzer** - `d7d593f` (feat)
2. **Task 2: Create DocCoverageAnalyzer and unit tests for all three analyzers** - `adff942` (feat)

## Files Created/Modified
- `src/DocAgent.Analyzers/DocAgent.Analyzers.csproj` - netstandard2.0 analyzer project
- `src/DocAgent.Analyzers/ExcludeFromDocCoverageAttribute.cs` - Suppression attribute with [Conditional]
- `src/DocAgent.Analyzers/DocParity/DocParityAnalyzer.cs` - DOCAGENT001: missing XML doc detection
- `src/DocAgent.Analyzers/SuspiciousEdit/SuspiciousEditAnalyzer.cs` - DOCAGENT002: semantic change heuristics
- `src/DocAgent.Analyzers/Coverage/DocCoverageAnalyzer.cs` - DOCAGENT003: coverage threshold enforcement
- `tests/DocAgent.Tests/Analyzers/DocParityAnalyzerTests.cs` - 4 tests for doc parity
- `tests/DocAgent.Tests/Analyzers/SuspiciousEditAnalyzerTests.cs` - 3 tests for suspicious edits
- `tests/DocAgent.Tests/Analyzers/DocCoverageAnalyzerTests.cs` - 3 tests for coverage threshold

## Decisions Made
- RS2008 (release tracking) and RS1032 (message format) suppressed via NoWarn — not needed for internal analyzers
- CompilationEnd custom tag added to DocCoverageAnalyzer descriptor as required by EnforceExtendedAnalyzerRules

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed nullable reference warnings in analyzer code**
- **Found during:** Task 1
- **Issue:** GetDocumentationCommentXml() returns nullable string; strict nullable context caused CS8602/CS8604
- **Fix:** Used null-forgiving operator and null-check pattern instead of IsNullOrEmpty
- **Files modified:** DocParityAnalyzer.cs, SuspiciousEditAnalyzer.cs
- **Verification:** Build succeeds with zero warnings

**2. [Rule 3 - Blocking] Suppressed RS2008 and RS1032 analyzer meta-rules**
- **Found during:** Task 1 (build), Task 2 (build)
- **Issue:** EnforceExtendedAnalyzerRules requires release tracking files and strict message formatting
- **Fix:** Added RS2008;RS1032 to NoWarn in csproj; added CompilationEnd custom tag to descriptor
- **Files modified:** DocAgent.Analyzers.csproj, DocCoverageAnalyzer.cs
- **Verification:** Build succeeds with zero errors

---

**Total deviations:** 2 auto-fixed (1 bug, 1 blocking)
**Impact on plan:** Both auto-fixes necessary for correctness. No scope creep.

## Issues Encountered
- Bitdefender file locks on test DLLs required killing testhost.exe between test runs (transient Windows AV issue)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Analyzers ready for NuGet packaging or direct project reference
- Coverage threshold configurable via `build_property.DocCoverageThreshold` in .globalconfig or Directory.Build.props

---
*Phase: 06-analysis-hosting*
*Completed: 2026-02-27*
