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

- [x] **EXTR-01**: Extract all declaration types (classes, interfaces, functions, enums, type aliases, constructors, methods, properties, fields) into `SymbolNode` graph
- [x] **EXTR-02**: Generate stable, deterministic `SymbolId` for every TypeScript symbol (source-stable, collision-free with C# IDs)
- [x] **EXTR-03**: Map TypeScript modules to `SymbolKind.Namespace` using relative file paths with `Contains` edges
- [x] **EXTR-04**: Extract inheritance (`extends`) and implementation (`implements`) edges as `SymbolEdge` relationships
- [x] **EXTR-05**: Map export visibility to accessibility (exported = public, non-exported = internal)
- [x] **EXTR-06**: Extract JSDoc/TSDoc comments into `DocComment` (summary, `@param`, `@returns`, `@example`, `@throws`, `@see`, `@remarks`)
- [x] **EXTR-07**: Capture source spans (file path + line range) for every extracted symbol
- [x] **EXTR-08**: Filter source files to project sources only — exclude `.d.ts` from `node_modules` to prevent snapshot bloat

### MCP Integration

- [x] **MCPI-01**: `ingest_typescript` MCP tool accepting tsconfig.json path with PathAllowlist security enforcement
- [ ] **MCPI-02**: All 14 existing MCP tools produce correct results when querying TypeScript snapshots
- [x] **MCPI-03**: Incremental TypeScript ingestion via SHA-256 file hashing — only re-parse changed files
- [ ] **MCPI-04**: BM25 search tokenizer handles camelCase (TS convention) alongside PascalCase (C#)

### Verification

- [x] **VERF-01**: Golden-file determinism tests — same TS project produces identical snapshot on repeated ingestion
- [x] **VERF-02**: Cross-tool validation — all 14 MCP tools tested against TypeScript snapshots
- [x] **VERF-03**: Security validation — PathAllowlist, no absolute path leaks in SymbolNode.Span, audit logging
- [x] **VERF-04**: Performance profiling on large (500+ file) TS projects with baseline thresholds

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
| Cross-language edges (C# <-> TypeScript) | Separate snapshots per language for v2.0; no integration needed |
| TypeScript-specific model extensions | Natural mappings first; union/mapped/conditional types stored as strings |
| Tree-sitter grammar approach | TypeScript Compiler API provides full type info; Tree-sitter is shallower |
| Warm sidecar process pooling | Cold-start per ingestion is simpler and avoids memory leak risk |
| `ts-morph` wrapper library | Unnecessary abstraction over TypeScript Compiler API |
| Non-tsconfig entry points | vite.config, next.config etc. deferred; tsconfig.json covers v2.0 |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SIDE-01 | Phase 28 | Pending |
| SIDE-02 | Phase 28 | Pending |
| SIDE-03 | Phase 28 | Pending |
| SIDE-04 | Phase 28 | Pending |
| EXTR-01 | Phase 29 | Complete |
| EXTR-02 | Phase 29 | Complete |
| EXTR-03 | Phase 29 | Complete |
| EXTR-04 | Phase 29 | Complete |
| EXTR-05 | Phase 29 | Complete |
| EXTR-06 | Phase 29 | Complete |
| EXTR-07 | Phase 29 | Complete |
| EXTR-08 | Phase 29 | Complete |
| MCPI-01 | Phase 30 | Complete |
| MCPI-02 | Phase 30 | Pending |
| MCPI-03 | Phase 30 | Complete |
| MCPI-04 | Phase 30 | Pending |
| VERF-01 | Phase 31 | Complete |
| VERF-02 | Phase 31 | Complete |
| VERF-03 | Phase 31 | Complete |
| VERF-04 | Phase 31 | Complete |

**Coverage:**
- v2.0 requirements: 20 total
- Mapped to phases: 20
- Unmapped: 0

---
*Requirements defined: 2026-03-08*
*Last updated: 2026-03-08 after roadmap creation*
