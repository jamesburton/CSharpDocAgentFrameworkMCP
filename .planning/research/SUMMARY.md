# Project Research Summary

**Project:** DocAgentFramework v2.0 -- TypeScript Language Support
**Domain:** Polyglot symbol extraction and documentation analysis via MCP server
**Researched:** 2026-03-08
**Confidence:** HIGH

## Executive Summary

DocAgentFramework v2.0 adds TypeScript symbol extraction to an existing, production-stable C# symbol graph and MCP server. The recommended approach is a Node.js sidecar process that uses the TypeScript Compiler API (`ts.createProgram`, `TypeChecker`) to extract symbols, communicating with the C# host via NDJSON over stdin/stdout. This is the same IPC pattern the MCP server already uses with its clients. The existing `SymbolNode`/`SymbolEdge`/`SymbolGraphSnapshot` model maps naturally to TypeScript constructs -- 10 of 14 `SymbolKind` values have direct TypeScript equivalents, and all 7 `SymbolEdgeKind` values apply without modification. No existing MCP tools, indexes, or storage require changes; TypeScript snapshots flow through the same `SnapshotStore` and `BM25SearchIndex` as C# snapshots.

The integration surface is deliberately narrow: one new NuGet package (`Aspire.Hosting.JavaScript` 13.1.2), one new Node.js project with a single runtime dependency (`typescript ~5.9`), one new C# service class (`TypeScriptIngestionService`), and one new MCP tool (`ingest_typescript`). The sidecar uses cold-start process isolation (spawn per request, OS reclaims memory on exit), avoiding the memory management complexity of a long-lived TypeScript compiler process. This mirrors proven patterns already in the codebase.

The primary risks are: (1) SymbolId instability -- TypeScript has no standard equivalent to C#'s `GetDocumentationCommentId()`, so a deterministic ID scheme must be designed and locked before any extraction code is written; (2) `.d.ts` phantom symbol pollution -- `ts.Program.getSourceFiles()` returns everything the compiler loaded, including all `node_modules` declarations, which must be filtered to project source files only; and (3) TypeScript 7's Go rewrite, which will eventually replace the JavaScript Compiler API. Pinning to TS 5.9.x and abstracting compiler interaction behind an interface limits the blast radius. All three risks are well-understood and have concrete mitigation strategies.

## Key Findings

### Recommended Stack

The existing C# stack (Roslyn, Lucene.Net, MessagePack, MCP SDK, Aspire, OpenTelemetry) is unchanged. New additions are minimal and well-verified.

**Core technologies:**
- **Node.js 24.x LTS** (Krypton): Runtime for TypeScript sidecar -- Active LTS through Oct 2026, first-class Aspire orchestration via `AddNodeApp()`
- **TypeScript ~5.9.x**: Compiler API for symbol extraction -- stable API surface since TS 2.x, pinned to avoid TS 6.0 breaking changes
- **Aspire.Hosting.JavaScript 13.1.2**: Aspire integration for Node.js lifecycle management -- replaces deprecated `Aspire.Hosting.NodeJs`
- **NDJSON over stdin/stdout**: IPC protocol -- zero dependencies, matches existing MCP stdio pattern, proven in codebase
- **esbuild ^0.25**: Bundle sidecar to single `dist/index.js` -- sub-second builds, zero config
- **vitest ^3**: Test runner for sidecar -- fast, native ESM+TS, standard for Node.js in 2026

**Critical version constraint:** Do NOT use `Aspire.Hosting.NodeJs` (deprecated). Do NOT use `ts-morph` (unnecessary abstraction). Do NOT add HTTP/gRPC frameworks (stdin/stdout is sufficient).

### Expected Features

**Must have (table stakes):**
- Symbol extraction for all declaration types (classes, interfaces, functions, enums, type aliases, constructors, methods, properties, fields)
- JSDoc/TSDoc extraction (summary, `@param`, `@returns`, `@example`, `@throws`, `@see`, `@remarks`)
- Inheritance and implementation edges (`extends`, `implements`)
- Module export structure (export = public, non-export = internal)
- Source spans (file path + line range for every symbol)
- Stable, deterministic SymbolId generation
- tsconfig.json as project entry point
- Incremental ingestion via SHA-256 file hashing (reuse existing infrastructure)
- `ingest_typescript` MCP tool
- Deterministic snapshot output (same input = identical output)

**Should have (differentiators):**
- Full JSDoc/TSDoc tag extraction (beyond just summary)
- Ambient `.d.ts` declaration support (stub nodes)
- Re-export tracking (`export { X } from './y'`)
- Overload signature capture
- Monorepo multi-tsconfig discovery

**Defer (v2.1+):**
- Declaration merging (multi-span symbols)
- Function overload signatures
- Modifier flags (`abstract`, `static`, `readonly`)
- Decorator extraction
- Cross-language edges (C# <-> TS)
- Union/intersection/mapped/conditional type decomposition

### Architecture Approach

The architecture follows a sidecar pattern: C# spawns a Node.js child process per ingestion request, sends an NDJSON request on stdin with the tsconfig.json path, and receives a SymbolGraphSnapshot-shaped JSON response on stdout. The C# side deserializes into existing domain types, applies `SymbolSorter` for deterministic ordering, and feeds the snapshot into `SnapshotStore` and `BM25SearchIndex`. All 14 existing MCP tools work against TypeScript snapshots without modification. The only existing files that change are `AppHost/Program.cs` (3-5 lines), `ServiceCollectionExtensions.cs` (2-3 lines), `DocAgentServerOptions.cs` (1-2 lines), and `Directory.Packages.props` (1 line).

**Major components:**
1. **ts-symbol-extractor** (NEW Node.js project) -- reads tsconfig.json, walks TS Compiler API, emits SymbolGraphSnapshot JSON on stdout
2. **TypeScriptIngestionService** (NEW C# class) -- spawns sidecar, sends request, deserializes response, feeds into SnapshotStore + SearchIndex
3. **ingest_typescript MCP tool** (NEW C# tool handler) -- validates path via PathAllowlist, delegates to TypeScriptIngestionService
4. **AppHost wiring** (MODIFIED) -- registers Node.js sidecar as Aspire resource via `AddNodeApp()`

**Key patterns to follow (already established in codebase):**
- `PipelineOverride` seam for testing without Node.js
- `PathAllowlist` validation before any pipeline work
- Deterministic output via `SymbolSorter`
- Content hash computed by `SnapshotStore`, not the extractor
- Per-path semaphore serialization for concurrent ingestion

### Critical Pitfalls

1. **SymbolId instability** -- TypeScript has no `GetDocumentationCommentId()` equivalent. Design a deterministic scheme from source-stable properties (file path + AST declaration chain + parameter types). Golden-file test required. Recovery cost is HIGH if wrong.
2. **`.d.ts` phantom symbol pollution** -- `getSourceFiles()` includes all `node_modules` declarations. Filter to `getRootFileNames()` + check `isDeclarationFile`. Without this, snapshots bloat 10-100x.
3. **stdout corruption** -- Any `console.log()` or library warning on stdout breaks NDJSON deserialization. Route ALL logging to stderr, use `--no-warnings` flag.
4. **Module-to-namespace mapping** -- TypeScript modules (files) must map to `SymbolKind.Namespace` using relative file paths. Wrong choice here restructures the entire graph; recovery cost is HIGH.
5. **Path alias resolution** -- `baseUrl`/`paths` in tsconfig.json create import aliases. Always use `sourceFile.fileName` (resolved absolute path), never import specifiers. Otherwise: duplicate or missing symbols.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Sidecar Scaffold and IPC Protocol
**Rationale:** The Node.js project structure, NDJSON protocol, and C# process spawning are prerequisites for everything else. This phase establishes the communication contract and validates Node.js availability.
**Delivers:** Working Node.js project that accepts an NDJSON request on stdin and returns a stub response on stdout; C# `TypeScriptIngestionService` that spawns the process and deserializes the response; startup validation for Node.js; PROTOCOL.md contract document.
**Addresses:** tsconfig.json as entry point, NDJSON protocol, sidecar lifecycle
**Avoids:** stdout pollution (establish stderr-only logging from first line of code), Node.js not found (startup validation)

### Phase 2: Core Symbol Extraction
**Rationale:** This is the critical path and highest-risk phase. SymbolId design, SymbolKind mapping, source file filtering, and path normalization must all be correct here. Every downstream consumer depends on these decisions.
**Delivers:** Complete symbol extraction for all declaration types; stable SymbolId generation; module-to-namespace mapping; JSDoc/TSDoc extraction; source spans; deterministic output.
**Addresses:** All table-stakes features except incremental ingestion and MCP tool
**Avoids:** SymbolId instability (golden-file tests), `.d.ts` phantom symbols (source file filtering), path alias bugs (resolved path normalization), SymbolKind mapping gaps (mapping table locked before coding)

### Phase 3: MCP Integration and Incremental Ingestion
**Rationale:** With extraction working, wire it into the MCP tool surface and add incremental ingestion for real-world usability.
**Delivers:** `ingest_typescript` MCP tool; incremental ingestion via SHA-256 file hashing; Aspire AppHost wiring; full end-to-end pipeline from `ingest_typescript` call through all 14 query tools.
**Addresses:** `ingest_typescript` tool, incremental ingestion, Aspire integration
**Avoids:** Sidecar lifecycle issues (cold-start with timeout + CancellationToken), PathAllowlist bypass (validate tsconfig.json path before spawning)

### Phase 4: Verification and Hardening
**Rationale:** Determinism, search tokenization, cross-tool validation, and performance profiling require the full pipeline to be functional. This phase catches "looks done but isn't" issues.
**Delivers:** Golden-file determinism tests; BM25 camelCase tokenization validation; diff/change review tool verification with TS snapshots; performance profiling with large (500+ file) projects; security validation (PathAllowlist, no absolute paths in SymbolNode.Span).
**Addresses:** Deterministic snapshots, search quality, security hardening
**Avoids:** IPC serialization bottlenecks on large projects, false diff results, search tokenization mismatches

### Phase Ordering Rationale

- **Phases 1-2 form the critical path.** Phase 2 (symbol extraction) is the largest and riskiest work. Phase 1 gives it a working scaffold to develop against. These cannot be reordered.
- **Phase 3 depends on Phase 2** because the MCP tool needs working extraction. Aspire wiring (Phase 3) is independent of extraction logic but makes sense to group with the integration work.
- **Phase 4 must come last** because verification requires the full pipeline. However, golden-file tests for SymbolId stability should be written IN Phase 2, not deferred.
- **Architecture supports parallelism:** Phase 1's C# side (TypeScriptIngestionService) and Node.js side (project scaffold) can be built concurrently. Phase 2's JSON contract types (C#) can be built alongside the TS walker.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 2:** SymbolId design is a critical, irreversible decision. Needs a design document and review before implementation begins. TSDoc vs JSDoc parsing strategy needs testing against real-world projects.

Phases with standard patterns (skip research-phase):
- **Phase 1:** NDJSON stdin/stdout IPC and Process.Start are well-documented, established patterns already used in this codebase.
- **Phase 3:** MCP tool registration, Aspire wiring, and incremental ingestion all follow existing patterns in the codebase (copy from `IngestionTools.cs`, `AppHost/Program.cs`, `IncrementalIngestionEngine`).
- **Phase 4:** Standard testing and verification work. No novel patterns.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All packages verified on npm/NuGet with published dates. Version compatibility matrix confirmed. Aspire.Hosting.JavaScript 13.1.2 published 2026-02-26. |
| Features | HIGH | Existing SymbolNode/SymbolEdge model inspected directly. 10 of 14 SymbolKind values map naturally. Feature dependencies are clear. |
| Architecture | HIGH | Sidecar + stdin/stdout NDJSON matches existing MCP pattern. Component boundaries defined. Build order dependencies mapped. Minimal changes to existing code. |
| Pitfalls | HIGH | Pitfalls verified via TypeScript compiler issue trackers, official docs, and direct codebase analysis. SymbolId and .d.ts filtering risks are well-documented in the TS community. |

**Overall confidence:** HIGH

### Gaps to Address

- **TypeAlias SymbolKind:** FEATURES.md recommends adding `SymbolKind.TypeAlias = 14`; ARCHITECTURE.md and PITFALLS.md recommend reusing `SymbolKind.Type` and deferring new enum values. Decision needed during Phase 2 planning -- leaning toward adding the enum value since it is a one-line, backward-compatible change.
- **`@microsoft/tsdoc` package:** PITFALLS.md recommends it for JSDoc/TSDoc parsing; STACK.md does not list it as a dependency. Evaluate during Phase 2 whether TypeScript's built-in `symbol.getDocumentationComment()` + `ts.getJSDocTags()` are sufficient, or if `@microsoft/tsdoc` is needed.
- **Aspire package name discrepancy:** ARCHITECTURE.md references the deprecated `Aspire.Hosting.NodeJs` in code examples while STACK.md correctly identifies `Aspire.Hosting.JavaScript` as the replacement. Use `Aspire.Hosting.JavaScript` exclusively.
- **BM25 camelCase tokenization:** PITFALLS.md flags that the tokenizer must handle camelCase (TS convention) not just PascalCase (C# convention). Verify during Phase 4 that existing `CamelCaseTokenizer` handles both -- if it already splits on case transitions, this is a non-issue.
- **TS Compiler `noCheck` viability:** PITFALLS.md mentions `noCheck: true` could skip type-checking for 2-3x speed gain, but the TypeChecker is needed for type resolution. Needs empirical testing during Phase 2.

## Sources

### Primary (HIGH confidence)
- TypeScript Compiler API Wiki -- symbol extraction API surface, `ts.createProgram`, `TypeChecker`
- TypeScript 5.9 release notes -- latest stable compiler version
- Node.js 24 LTS release schedule -- runtime version and support timeline
- Aspire.Hosting.JavaScript 13.1.2 on NuGet -- published 2026-02-26, verified package
- NDJSON specification -- protocol format
- TypeScript native port announcement (Project Corsa) -- TS 7.0 Go rewrite impact
- Direct codebase reads: `Symbols.cs`, `RoslynSymbolGraphBuilder.cs`, `IngestionService.cs`, `SnapshotStore.cs`, `AppHost/Program.cs`, `Directory.Packages.props`

### Secondary (MEDIUM confidence)
- Aspire JavaScript integration docs -- `AddNodeApp()` API patterns
- TypeDoc GitHub -- reference for TS doc extraction approaches
- TSDoc specification -- tag format differences from JSDoc
- Community IPC patterns (C# to Node.js stdin/stdout) -- process lifecycle management

---
*Research completed: 2026-03-08*
*Ready for roadmap: yes*
