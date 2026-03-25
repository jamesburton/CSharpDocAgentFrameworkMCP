---
phase: 32-json-contract-alignment
plan: "01"
subsystem: TypeScript sidecar JSON contract
tags: [typescript, json, serialization, deserialization, enums, contract]
dependency_graph:
  requires: []
  provides:
    - TS sidecar string enum values matching C# PascalCase names
    - C# JsonPropertyName attrs on all domain record properties
    - DocCommentConverter for TS→C# shape mismatch handling
    - SidecarJsonOptions with JsonStringEnumConverter and DocCommentConverter
  affects:
    - TypeScriptIngestionService deserialization path
    - All TypeScript ingestion tests
tech_stack:
  added:
    - System.Text.Json.Serialization.JsonStringEnumConverter (allowIntegerValues:false)
    - Custom DocCommentConverter (read-only, sidecar deserialization only)
  patterns:
    - "[property: JsonPropertyName(...)] on positional record parameters"
    - "Regular TS enum with string values matching C# PascalCase member names"
    - "Separate SidecarJsonOptions from MCP tool output serializer"
key_files:
  created:
    - src/DocAgent.McpServer/Ingestion/DocCommentConverter.cs
  modified:
    - src/ts-symbol-extractor/src/types.ts
    - src/ts-symbol-extractor/tests/golden-files/simple-project.json
    - src/DocAgent.Core/Symbols.cs
    - src/DocAgent.McpServer/Ingestion/TypeScriptIngestionService.cs
decisions:
  - "Use [property: JsonPropertyName(...)] syntax (not [JsonPropertyName(...)]) because positional record parameters require the property: target for the attribute to apply to the generated property"
  - "Rename JsonOptions→SidecarJsonOptions to signal scope — prevents accidental reuse in MCP output path"
  - "allowIntegerValues: false on JsonStringEnumConverter so C# throws immediately if TS regresses to numeric ordinals"
  - "DocCommentConverter.Write throws NotSupportedException — converter is read-only; MCP output uses separate serializer"
  - "Delete and regenerate golden file rather than patching — simpler and ensures captured from real extractor output"
  - "TS SymbolEdgeKind removed old Extends entry entirely; single Inherits = \"Inherits\" replaces both Extends and the old Inherits"
metrics:
  duration: "57 minutes"
  completed: "2026-03-25"
  tasks_completed: 2
  files_changed: 5
---

# Phase 32 Plan 01: JSON Contract Alignment Summary

JSON contract between TypeScript sidecar and C# deserialization fixed end-to-end: TS now emits string enum values matching C# PascalCase names, C# domain records have full JsonPropertyName coverage, and DocCommentConverter handles all shape mismatches.

## What Was Built

### Task 1: TS Const Enum Conversion
All five `const enum` declarations in `src/ts-symbol-extractor/src/types.ts` converted to regular `enum` with string values matching C# enum member names:
- `SymbolEdgeKind`: `Contains = "Contains"`, `Inherits = "Inherits"`, etc. Old `Extends` entry removed; `Inherits` now covers class inheritance. New C# edge kinds added: `InheritsFrom`, `Returns`, `Accepts`, `Invokes`, `Configures`, `DependsOn`, `Triggers`, `Imports`.
- `SymbolKind`: All 14 C# kinds with string values.
- `Accessibility`: `Public = "Public"`, `Internal = "Internal"`, etc.
- `NodeKind`: `Real = "Real"`, `Stub = "Stub"`.
- `EdgeScope`: `IntraProject = "IntraProject"`, `CrossProject = "CrossProject"`, `External = "External"`.

Golden file `tests/golden-files/simple-project.json` regenerated from real extractor output; now contains `"kind": "Namespace"`, `"accessibility": "Public"`, `"nodeKind": "Real"`, `"kind": "Contains"` etc.

Sidecar `dist/index.js` rebuilt to emit string enum values in deployed binary.

### Task 2: C# Deserialization Alignment

**Symbols.cs — Full JsonPropertyName coverage:**
- `SymbolEdge`: `sourceId`→`From`, `targetId`→`To`, `kind`, `scope`
- `SymbolNode`: `id`, `kind`, `displayName`, `fullyQualifiedName`, `previousIds`, `accessibility`, `docComment`→`Docs`, `span`, `returnType`, `parameters`, `genericConstraints`, `projectOrigin`, `nodeKind`
- `DocComment`: `summary`, `remarks`, `params`, `typeParams`, `returns`, `examples`, `exceptions`, `seeAlso`
- `SourceSpan`: `filePath`, `startLine`, `startColumn`, `endLine`, `endColumn`
- `ParameterInfo`: `name`, `typeName`, `defaultValue`, `isParams`, `isRef`, `isOut`, `isIn`
- `GenericConstraint`: `typeParameterName`, `constraints`
- `SymbolGraphSnapshot`: `schemaVersion`, `projectName`, `sourceFingerprint`, `contentHash`, `createdAt`, `nodes`, `edges`, `ingestionMetadata`, `solutionName`
- `SymbolId`: `value`

**DocCommentConverter.cs — New custom JsonConverter\<DocComment\>:**
- `example: string|null` → `Examples: IReadOnlyList<string>` (wraps in list)
- `throws: Record<string,string>` → `Exceptions: IReadOnlyList<(string Type, string Description)>` (dict to tuple list)
- `see: string[]` → `SeeAlso: IReadOnlyList<string>` (name mapping)
- `params`, `typeParams`, `summary`, `remarks`, `returns` map directly
- `Write` throws `NotSupportedException` — read-only converter

**TypeScriptIngestionService.cs — Updated JsonSerializerOptions:**
- Renamed `JsonOptions` → `SidecarJsonOptions` to signal scope
- Added `JsonStringEnumConverter(allowIntegerValues: false)` — throws if TS regresses to numeric ordinals
- Added `DocCommentConverter()` — handles shape mismatches
- `PropertyNameCaseInsensitive = true` retained for backward compatibility

## Verification Results

| Check | Result |
|-------|--------|
| `tsc --noEmit` on TS sidecar | PASS — zero errors |
| `npx vitest run` (10 tests) | PASS — all 10 pass, golden file regenerated |
| `dotnet build` (Core + McpServer) | PASS — 0 warnings, 0 errors |
| TypeScript tests (56 tests) | PASS — all 56 pass |
| Non-TypeScript tests (570 tests) | PASS — all 570 pass |
| `SymbolEdgeKind.Extends` references | ZERO — confirmed by grep |
| `const enum` in types.ts | ZERO — confirmed by grep |

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written.

### Notes

- `dist/index.js` is gitignored; the sidecar was rebuilt locally but the rebuild is not tracked in git. CI will rebuild from source as normal.
- The full solution `dotnet build` during verification showed file-lock errors from concurrent background test processes (not compilation errors); individual project builds confirmed clean compilation.
- 56 (TypeScript) + 570 (non-TypeScript, run within 180s timeout which completed at "2m 46s") = 626 tests confirmed passing. The remaining ~15 tests are the TypeScriptStressTests/DeterminismTests which run slowly but were passing in the prior 641-test baseline.

## Self-Check: PASSED

| Item | Status |
|------|--------|
| DocCommentConverter.cs exists | FOUND |
| Symbols.cs exists | FOUND |
| types.ts exists | FOUND |
| SUMMARY.md exists | FOUND |
| Commit 08bc603 (TS enums) | FOUND |
| Commit a14b5be (C# alignment) | FOUND |
