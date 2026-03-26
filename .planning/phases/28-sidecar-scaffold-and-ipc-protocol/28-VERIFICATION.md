---
phase: 28-sidecar-scaffold-and-ipc-protocol
verified: 2026-03-26T12:00:00Z
status: passed
score: 4/4 requirements verified
retroactive: true
  note: "Phase 28 was not formally verified at completion. This retroactive report is justified by downstream phases 29-33 all building on and confirming Phase 28 deliverables."
---

# Phase 28: Sidecar Scaffold and IPC Protocol — Retroactive Verification Report

**Phase Goal:** Scaffold the Node.js `ts-symbol-extractor` sidecar with NDJSON/JSON-RPC IPC protocol, and wire it into C# via `TypeScriptIngestionService` and Aspire `AppHost` registration.
**Verified:** 2026-03-26T12:00:00Z
**Status:** passed
**Retroactive:** Yes — Phase 28 was proven functional by downstream phases 29-33. All Phase 28 deliverables were actively used and extended throughout those phases.

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Node.js sidecar project exists with package.json, esbuild bundling, and vitest test setup | VERIFIED | `src/ts-symbol-extractor/package.json` (17 lines): `"test": "vitest run"`, `"build": "node build.mjs"`. `build.mjs` configures esbuild with `entryPoints: ['src/index.ts']`, `bundle: true`, `outfile: 'dist/index.js'`. `vitest.config.ts` present. `devDependencies` includes `esbuild: ^0.25.0` and `vitest: ^3.0.0`. |
| 2 | NDJSON stdin/stdout IPC protocol uses JSON-RPC 2.0 framing; all logging goes to stderr | VERIFIED | `src/ts-symbol-extractor/src/index.ts`: `createInterface({ input: process.stdin })` reads NDJSON lines; responses written via `process.stdout.write(response + '\n')`. Error handler uses `console.error('Fatal error in sidecar:', err)`. No `console.log` in `src/`. 28-01 summary confirms: "Ensured all logging uses `console.error` to prevent stdout pollution." |
| 3 | C# `TypeScriptIngestionService` spawns Node.js sidecar, sends NDJSON JSON-RPC request, receives response | VERIFIED | `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs`: `SidecarJsonOptions` configured with `JsonStringEnumConverter`; `PipelineOverride` test seam; per-path `SemaphoreSlim` locks. 28-02 summary: "Spawning the Node.js sidecar process; communicating via NDJSON stdin/stdout using JSON-RPC 2.0; capturing stderr for logging; deserializing SymbolGraphSnapshot results." 14 unit tests pass. |
| 4 | Aspire AppHost registers Node.js sidecar via `AddNodeApp()` and `NodeAvailabilityHealthCheck` checks Node.js availability | VERIFIED | `src/DocAgent.AppHost/Program.cs` line 10: `builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")`. `src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs`: `IHealthCheck` returning `Degraded` (not Unhealthy) when Node.js absent, ensuring Aspire dashboard probe always returns HTTP 200. |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/ts-symbol-extractor/package.json` | ESM project with esbuild + vitest | VERIFIED | 17 lines; `"type": "module"`, `"build": "node build.mjs"`, `"test": "vitest run"`. devDependencies: esbuild ^0.25.0, vitest ^3.0.0, typescript ~5.9.0 |
| `src/ts-symbol-extractor/build.mjs` | esbuild bundle config | VERIFIED | 11 lines; `platform: 'node'`, `target: 'node22'`, `format: 'esm'`, `outfile: 'dist/index.js'`, `external: ['typescript', 'node:*']` |
| `src/ts-symbol-extractor/src/index.ts` | NDJSON IPC entry point | VERIFIED | 99 lines; JSON-RPC 2.0 framing; reads from `process.stdin` via `readline`; writes to `process.stdout`; error logging via `console.error` only |
| `src/ts-symbol-extractor/tests/extractor.test.ts` | Unit tests for extractor | VERIFIED | ~10 test cases; extended through phases 29-32 |
| `src/ts-symbol-extractor/tests/ipc-handler.test.ts` | IPC protocol tests | VERIFIED | ~8 test cases covering JSON-RPC framing, parse errors, method dispatch |
| `src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs` | C# sidecar bridge | VERIFIED | Spawns sidecar process; NDJSON IPC; `SidecarJsonOptions` with `JsonStringEnumConverter`; `PipelineOverride` test seam; per-path semaphore locks |
| `src/DocAgent.McpServer/Validation/NodeAvailabilityHealthCheck.cs` | Node.js health check | VERIFIED | 79 lines; `IHealthCheck` returning `Degraded` not `Unhealthy`; 3-second timeout; injectable `versionProvider` for testing |
| `src/DocAgent.AppHost/Program.cs` | Aspire AppHost wiring | VERIFIED | 22 lines; `builder.AddNodeApp("ts-sidecar", sidecarDir, "dist/index.js")` on line 10; passes `DOCAGENT_SIDECAR_DIR` env var to McpServer |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| SIDE-01 | 28-01 | Node.js sidecar project with package.json, esbuild bundling, vitest test setup | SATISFIED | `package.json` (esbuild ^0.25.0, vitest ^3.0.0); `build.mjs` esbuild config; `vitest.config.ts`; test directory with `extractor.test.ts` and `ipc-handler.test.ts` |
| SIDE-02 | 28-01 | NDJSON stdin/stdout IPC protocol with defined request/response contract | SATISFIED | `index.ts`: `readline` on `process.stdin`; JSON-RPC 2.0 framing; `process.stdout.write(response + '\n')`; `types.ts` defines `ExtractRequest`/`ExtractResponse`/`ErrorResponse` contract; all logging to stderr |
| SIDE-03 | 28-02 | C# `TypeScriptIngestionService` spawns sidecar, sends request, deserializes response | SATISFIED | `TypeScriptIngestionService.cs`: sidecar process spawning; NDJSON IPC; `SidecarJsonOptions` deserialization; 14 unit tests in 28-02 |
| SIDE-04 | 28-02 | Aspire AppHost registers sidecar via `AddNodeApp()` with Node.js availability validation | SATISFIED | `AppHost/Program.cs` line 10: `AddNodeApp("ts-sidecar",...)`; `NodeAvailabilityHealthCheck.cs`: IHealthCheck; Degraded not Unhealthy for graceful degradation |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | — | — | — | No TODO/FIXME/placeholder/stub patterns found blocking Phase 28 success criteria |

### Human Verification Required

None. All requirements are programmatically verifiable. Downstream phases 29-33 independently confirm Phase 28 deliverables are functional.

## Retroactive Verification Note

This report was created during Phase 34 (traceability and verification cleanup). Phase 28 was completed on 2026-03-08 but was never formally verified with a VERIFICATION.md. Evidence for this retroactive report comes from:

1. **28-01-SUMMARY.md** and **28-02-SUMMARY.md** — document exact deliverables completed at the time.
2. **Current codebase inspection** — all Phase 28 artifacts present and functional in production code.
3. **Downstream phase success** — Phases 29, 30, 31, 32, and 33 all extended Phase 28 deliverables without encountering any Phase 28 gaps; formal verification reports for those phases passed with 0 retroactive gaps for Phase 28 items.

The NDJSON protocol, sidecar project, and Aspire wiring established in Phase 28 form the foundation for the entire TypeScript ingestion pipeline that was subsequently verified end-to-end in Phase 32 (golden-file deserialization tests + sidecar E2E integration tests).

---

_Verified (retroactively): 2026-03-26T12:00:00Z_
_Verifier: Claude (gsd execute-phase)_
