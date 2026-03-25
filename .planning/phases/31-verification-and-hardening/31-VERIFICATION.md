---
phase: 31-verification-and-hardening
verified: 2026-03-25T00:00:00Z
status: gaps_found
score: 6/10 must-haves verified
gaps:
  - truth: "No absolute paths are leaked in SymbolNode.Span fields"
    status: failed
    reason: "extractor.ts getSourceSpan() at line 284 uses sourceFile.fileName (absolute) directly in SourceSpan.filePath. The relativePath computed at line 56 is used only for node IDs and displayName, not for span.filePath."
    artifacts:
      - path: "src/ts-symbol-extractor/src/extractor.ts"
        issue: "getSourceSpan() returns { filePath: sourceFile.fileName.replace(/\\\\/g, '/') } — absolute OS path leaks into every SymbolNode.Span.FilePath"
    missing:
      - "In getSourceSpan(), pass the pre-computed relativePath (or projectRoot) and replace sourceFile.fileName with path.relative(projectRoot, sourceFile.fileName).replace(/\\\\/g, '/')"
      - "Add a test in TypeScriptRobustnessTests that asserts all Span.FilePath values do NOT start with '/' or match an absolute drive letter pattern"

  - truth: "All debug logs are removed from production-ready sidecar and service"
    status: failed
    reason: "src/ts-symbol-extractor/src/index.ts still contains two console.error debug statements on lines 10 and 71 that emit request/line details to stderr on every sidecar invocation."
    artifacts:
      - path: "src/ts-symbol-extractor/src/index.ts"
        issue: "Line 10: console.error(`Sidecar received: ${line.substring(0, 100)}...`); Line 71: console.error(`Sidecar processing line (length ${line.length}): ...`). Both fire on every request."
    missing:
      - "Remove lines 10 and 71 from index.ts (the line.10 console.error in handleRequest and line 71 in main's for-await loop)"
      - "Only the fatal error on line 97 (console.error('Fatal error in sidecar:', err)) should remain — it's in a catch block and fires only on crash"

  - truth: "PathAllowlist prevents ingestion of files outside the allowed directory"
    status: partial
    reason: "PathAllowlist enforcement exists in TypeScriptIngestionService (line 64) and is tested by TypeScriptRobustnessTests. However the key_link documented in 31-02-PLAN.md (from TypeScriptIngestionService to IngestionMetadata.cs via PathAllowlist check) is not wired — IngestionMetadata is not referenced in TypeScriptIngestionService at all. The enforcement works but does not use the contract the plan intended."
    artifacts:
      - path: "src/DocAgent.Core/IngestionMetadata.cs"
        issue: "IngestionMetadata.cs exists but TypeScriptIngestionService does not reference it — plan key_link is not wired as documented"
    missing:
      - "Clarify whether IngestionMetadata is intended to carry audit metadata. If so, wire TypeScriptIngestionService to populate IngestionMetadata after ingestion."
      - "Alternatively, update the 31-02-PLAN key_link to reflect the actual PathAllowlist usage pattern."

  - truth: "Audit logging records a TypeScriptIngested event with elapsed time and file count"
    status: failed
    reason: "TypeScriptIngestionService does not call AuditLogger directly. AuditFilter provides general tool-call logging (tool name, duration, success/fail) but no typed TypeScriptIngested event with file count metadata. VERF-03 required: 'AuditLogger records a TypeScriptIngested event with correct metadata (elapsed time, file count).'"
    artifacts:
      - path: "src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs"
        issue: "No AuditLogger dependency injected. No typed TypeScriptIngested event emitted."
      - path: "src/DocAgent.McpServer/Tools/IngestionTools.cs"
        issue: "IngestTypeScript tool handler does not call AuditLogger with file count after successful ingestion."
    missing:
      - "Inject AuditLogger into TypeScriptIngestionService (or IngestionTools.IngestTypeScript handler)"
      - "After successful ingestion, call audit.Log() or a typed audit.LogTypeScriptIngested(fileCount, elapsedMs) with the result metadata"
      - "Add a test in TypeScriptRobustnessTests verifying an audit log entry is written with the correct metadata"
---

# Phase 31: Verification and Hardening — Verification Report

**Phase Goal:** The TypeScript ingestion pipeline is proven deterministic, secure, and performant through comprehensive validation against real-world projects
**Verified:** 2026-03-25
**Status:** gaps_found
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Ingesting a 100+ file TypeScript project completes without errors | VERIFIED | TypeScriptStressTests.IngestTypeScript_LargeProject_CompletesWithoutErrors — 110-file synthetic project, 5 passing tests |
| 2 | Warm start (incremental hit) is significantly faster than cold start | VERIFIED | TypeScriptDeterminismTests.IngestTypeScript_IncrementalHit_SkipsWhenManifestUnchanged — second call returns Skipped=true with same SnapshotId |
| 3 | Two identical ingestions of the same TS project produce identical snapshot hashes | VERIFIED | TypeScriptDeterminismTests.BuildSnapshot_ProducesBytewiseIdenticalHash_WhenCalledTwice — fixed-timestamp snapshots produce byte-identical ContentHash |
| 4 | Searching symbols in a large TypeScript graph returns correct results with low latency | VERIFIED | TypeScriptDeterminismTests.Search_LargeTypeScriptGraph_CompletesUnder50ms — 951-node graph, < 50ms |
| 5 | Ingestion fails with a clear error for missing or invalid tsconfig.json | VERIFIED | TypeScriptRobustnessTests: handles_missing_tsconfig (TypeScriptIngestionException "tsconfig.json not found") and handles_invalid_tsconfig_json — both passing |
| 6 | TypeScript files with syntax errors are still partially ingested (best effort) | VERIFIED | TypeScriptRobustnessTests.IngestTypeScriptAsync_handles_ts_syntax_errors_gracefully — result.SymbolCount > 0, search hits non-empty |
| 7 | PathAllowlist prevents ingestion of files outside the allowed directory | VERIFIED (partial) | TypeScriptRobustnessTests.IngestTypeScriptAsync_throws_UnauthorizedAccessException_if_outside_allowlist passes; PathAllowlist check exists at line 64 of TypeScriptIngestionService. Plan key_link to IngestionMetadata.cs is unwired (see gaps). |
| 8 | No absolute paths are leaked in SymbolNode.Span fields | FAILED | extractor.ts:284 — getSourceSpan() sets filePath: sourceFile.fileName (absolute OS path). relativePath is used for IDs/displayName but not for span.filePath. No test covers this. |
| 9 | All debug logs are removed from production-ready sidecar and service | FAILED | src/ts-symbol-extractor/src/index.ts lines 10 and 71 contain active console.error statements emitting request content to stderr on every invocation |
| 10 | Audit logging records a TypeScriptIngested event with elapsed time and file count | FAILED | AuditFilter provides general per-tool logging; no typed TypeScriptIngested event with file count is emitted anywhere |

**Score:** 6/10 truths verified (4 failed/partial)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `tests/DocAgent.Tests/TypeScriptStressTests.cs` | Stress test for large TS project ingestion | VERIFIED | 361 lines, 5 substantive tests, PipelineOverride pattern, 110-file synthetic project |
| `tests/DocAgent.Benchmarks/TypeScriptIngestionBenchmarks.cs` | Benchmarking for cold/warm TS ingestion | VERIFIED | 181 lines, BenchmarkDotNet with MemoryDiagnoser and JsonExporter, two benchmarks |
| `tests/DocAgent.Tests/TypeScriptDeterminismTests.cs` | Snapshot determinism + 14 MCP tool round-trips | VERIFIED | 530 lines, 18 tests exercising 12 distinct MCP tools |
| `tests/DocAgent.Tests/TypeScriptRobustnessTests.cs` | Negative tests for error conditions | VERIFIED | 119 lines, 4 tests: allowlist denial, missing tsconfig, invalid JSON, syntax error resilience |
| `docs/Architecture.md` | Updated architecture docs with TS sidecar details | PARTIAL | Contains Node.js sidecar reference (line 17), ingest_typescript tool (line 102), NDJSON mention (line 108). Missing: dedicated TypeScript sidecar architecture section, NDJSON protocol definition, TypeScript symbol mapping strategy |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `tests/DocAgent.Tests/TypeScriptStressTests.cs` | `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs` | IngestTypeScriptAsync call | WIRED | Line 250: `await _tsService.IngestTypeScriptAsync(tsconfigPath, ...)` |
| `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs` | `src/DocAgent.Core/IngestionMetadata.cs` | PathAllowlist check | NOT_WIRED | TypeScriptIngestionService does not import or reference IngestionMetadata; PathAllowlist enforcement is implemented correctly but does not use IngestionMetadata |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| VERF-01 | 31-01 | Golden-file determinism tests — identical snapshot on repeated ingestion | SATISFIED | TypeScriptDeterminismTests.BuildSnapshot_ProducesBytewiseIdenticalHash_WhenCalledTwice + IncrementalHit test. REQUIREMENTS.md checkbox is marked [x]. |
| VERF-02 | 31-01 | Cross-tool validation — all 14 MCP tools tested against TypeScript snapshots | PARTIAL | TypeScriptDeterminismTests exercises 12 distinct MCP tools (search_symbols, get_symbol, get_references, find_implementations, get_doc_coverage, diff_snapshots, explain_project, review_changes, find_breaking_changes, explain_change, explain_solution, diff_solution_snapshots). Missing: ingest_project, ingest_solution, ingest_typescript tool-layer tests. REQUIREMENTS.md still shows [ ] (Pending). |
| VERF-03 | 31-02 | Security validation — PathAllowlist, no absolute path leaks in SymbolNode.Span, audit logging | BLOCKED | PathAllowlist: VERIFIED. Absolute path leaks in Span.filePath: FAILED (extractor.ts:284). Audit logging: FAILED (no typed TypeScriptIngested event). REQUIREMENTS.md still shows [ ] (Pending). |
| VERF-04 | 31-01 | Performance profiling on large (500+ file) TS projects with baseline thresholds | PARTIAL | Benchmark harness exists with VERF-04 thresholds documented (warm < 1500ms for 100 files). Stress tests use 110-file synthetic project. NOTE: Plan says "500+ file TS projects" but implementation uses 100–110 files. REQUIREMENTS.md checkbox is marked [x]. |

**ORPHANED requirements check:** VERF-02 and VERF-03 are claimed by phase 31 plans but remain unchecked [Pending] in REQUIREMENTS.md, confirming they are not fully satisfied.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `src/ts-symbol-extractor/src/index.ts` | 10 | `console.error(\`Sidecar received: ${line.substring(0,100)}...\`)` | Blocker | Emits partial request content to stderr on every production invocation; data exposure risk |
| `src/ts-symbol-extractor/src/index.ts` | 71 | `console.error(\`Sidecar processing line (length ${line.length}): ...\`)` | Blocker | Same as above — fires in the main request loop |
| `src/ts-symbol-extractor/src/extractor.ts` | 284 | `filePath: sourceFile.fileName.replace(/\\/g, '/')` | Blocker | Leaks absolute OS paths (e.g., `C:/Users/james/...`) into every SymbolNode.Span.FilePath in the snapshot |

### Human Verification Required

None. All verification items are verifiable programmatically via code inspection and test execution.

### Gaps Summary

Phase 31 has **four gaps** blocking full goal achievement:

**Gap 1 — Absolute path leak in SymbolNode.Span.FilePath (VERF-03 blocker)**
The sidecar extractor computes a correct `relativePath` (line 56 of extractor.ts) but passes the raw `sourceFile.fileName` (absolute path) to `getSourceSpan()`. Every `SymbolNode.Span.FilePath` in every TypeScript snapshot contains an absolute machine path. This is the most critical security gap.

**Gap 2 — Debug console.error statements remain in sidecar (plan task 3 incomplete)**
Two `console.error` statements on lines 10 and 71 of `index.ts` emit request metadata on every invocation. These are not fatal errors; they were debug instrumentation and should have been removed. They go to stderr (not stdout) so they do not corrupt the JSON-RPC protocol, but they constitute information disclosure and noise.

**Gap 3 — No typed audit event for TypeScript ingestion (VERF-03 partial)**
The plan required `AuditLogger` to record a `TypeScriptIngested` event with elapsed time and file count. The general `AuditFilter` wraps all tool calls with basic success/fail logging, but the `TypeScriptIngestionService` does not inject or call `AuditLogger` directly, and no file count metadata is recorded.

**Gap 4 — Architecture.md missing dedicated TypeScript documentation section (plan task 3 incomplete)**
`docs/Architecture.md` has scattered TypeScript references but lacks the required dedicated section covering: Node.js sidecar architecture, NDJSON protocol definition, and TypeScript symbol mapping strategy.

**Root cause grouping:** Gaps 2 and 4 share a root cause — plan 02, task 3 (Final Cleanup and Documentation Refresh) was not completed as specified. Gaps 1 and 3 are distinct security hardening items from plan 02, tasks 2 and 2 respectively.

---

_Verified: 2026-03-25_
_Verifier: Claude (gsd-verifier)_
