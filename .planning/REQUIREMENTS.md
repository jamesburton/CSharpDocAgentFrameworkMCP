# Requirements: DocAgentFramework

**Defined:** 2026-03-08
**Core Value:** Agents can query a stable, compiler-grade symbol graph of any .NET or TypeScript codebase via MCP tools, getting precise answers about types, members, relationships, and documentation.

## v2.0 Requirements

Requirements for TypeScript Language Support milestone. Each maps to roadmap phases.

### Sidecar Infrastructure

- [ ] **SIDE-01**: Node.js sidecar project (`ts-symbol-extractor`) with package.json, esbuild bundling, vitest test setup
- [ ] **SIDE-02**: NDJSON stdin/stdout IPC protocol with defined request/response contract
- [ ] **SIDE-03**: C# `TypeScriptIngestionService` that spawns Node.js sidecar, sends request, deserializes response into `SymbolGraphSnapshot`
- [ ] **SIDE-04**: Aspire AppHost registers Node.js sidecar via `AddNodeApp()` with startup validation for Node.js availability

### Symbol Extraction

- [ ] **EXTR-01**: Extract all declaration types (classes, interfaces, functions, enums, type aliases, constructors, methods, properties, fields) into `SymbolNode` graph
- [ ] **EXTR-02**: Generate stable, deterministic `SymbolId` for every TypeScript symbol (source-stable, collision-free with C# IDs)
- [ ] **EXTR-03**: Map TypeScript modules to `SymbolKind.Namespace` using relative file paths with `Contains` edges
- [ ] **EXTR-04**: Extract inheritance (`extends`) and implementation (`implements`) edges as `SymbolEdge` relationships
- [ ] **EXTR-05**: Map export visibility to accessibility (exported = public, non-exported = internal)
- [ ] **EXTR-06**: Extract JSDoc/TSDoc comments into `DocComment` (summary, `@param`, `@returns`, `@example`, `@throws`, `@see`, `@remarks`)
- [ ] **EXTR-07**: Capture source spans (file path + line range) for every extracted symbol
- [ ] **EXTR-08**: Filter source files to project sources only — exclude `.d.ts` from `node_modules` to prevent snapshot bloat

### MCP Integration

- [ ] **MCPI-01**: `ingest_typescript` MCP tool accepting tsconfig.json path with PathAllowlist security enforcement
- [ ] **MCPI-02**: All 14 existing MCP tools produce correct results when querying TypeScript snapshots
- [ ] **MCPI-03**: Incremental TypeScript ingestion via SHA-256 file hashing — only re-parse changed files
- [ ] **MCPI-04**: BM25 search tokenizer handles camelCase (TS convention) alongside PascalCase (C#)

### Verification

- [ ] **VERF-01**: Golden-file determinism tests — same TS project produces identical snapshot on repeated ingestion
- [ ] **VERF-02**: Cross-tool validation — all 14 MCP tools tested against TypeScript snapshots
- [ ] **VERF-03**: Security validation — PathAllowlist, no absolute path leaks in SymbolNode.Span, audit logging
- [ ] **VERF-04**: Performance profiling on large (500+ file) TS projects with baseline thresholds

## v2.1 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Advanced TypeScript

- **FUTR-01**: Declaration merging support (multi-span symbols across files)
- **FUTR-02**: Ambient `.d.ts` stub nodes (analogous to NuGet stub nodes for C#)
- **FUTR-03**: Re-export tracking (`export { X } from './y'`)
- **FUTR-04**: Union/intersection/mapped/conditional type decomposition
- **FUTR-05**: Decorator extraction
- **FUTR-06**: Warm sidecar process pooling (avoid cold-start overhead)
- **FUTR-07**: Monorepo multi-tsconfig discovery
- **FUTR-08**: Vite/other framework config entry points

## Out of Scope

| Feature | Reason |
|---------|--------|
| Cross-language edges (C# ↔ TypeScript) | Separate snapshots per language for v2.0; no integration needed |
| TypeScript-specific model extensions | Natural mappings first; union/mapped/conditional types stored as strings |
| Tree-sitter grammar approach | TypeScript Compiler API provides full type info; Tree-sitter is shallower |
| Warm sidecar process pooling | Cold-start per ingestion is simpler and avoids memory leak risk |
| `ts-morph` wrapper library | Unnecessary abstraction over TypeScript Compiler API |
| Non-tsconfig entry points | vite.config, next.config etc. deferred; tsconfig.json covers v2.0 |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SIDE-01 | — | Pending |
| SIDE-02 | — | Pending |
| SIDE-03 | — | Pending |
| SIDE-04 | — | Pending |
| EXTR-01 | — | Pending |
| EXTR-02 | — | Pending |
| EXTR-03 | — | Pending |
| EXTR-04 | — | Pending |
| EXTR-05 | — | Pending |
| EXTR-06 | — | Pending |
| EXTR-07 | — | Pending |
| EXTR-08 | — | Pending |
| MCPI-01 | — | Pending |
| MCPI-02 | — | Pending |
| MCPI-03 | — | Pending |
| MCPI-04 | — | Pending |
| VERF-01 | — | Pending |
| VERF-02 | — | Pending |
| VERF-03 | — | Pending |
| VERF-04 | — | Pending |

**Coverage:**
- v2.0 requirements: 20 total
- Mapped to phases: 0
- Unmapped: 20 ⚠️

---
*Requirements defined: 2026-03-08*
*Last updated: 2026-03-08 after initial definition*
