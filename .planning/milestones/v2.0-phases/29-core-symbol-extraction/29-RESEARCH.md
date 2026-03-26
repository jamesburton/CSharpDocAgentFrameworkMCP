# Research: Phase 29 Core Symbol Extraction

**Focus**: TypeScript Compiler API extraction logic, SymbolId design, and SymbolKind mapping.

## Key Goals
- Implement `extractSymbols(tsconfigPath)` replacing the stub.
- Extract classes, interfaces, functions, enums, type aliases, methods, properties, fields.
- Extract JSDoc/TSDoc (summary, @param, @returns, @remarks, etc.).
- Generate stable, deterministic `SymbolId`.
- Filter out `node_modules` and ambient declarations (unless specifically needed).
- Map TypeScript modules to `SymbolKind.Namespace`.

## Technical Approach
- Use `ts.createProgram` from `typescript` package.
- Use `program.getTypeChecker()` for symbol and type resolution.
- Walk `program.getSourceFiles()`, filtering for project source files only.
- Map TS `Symbol` and `Node` to `SymbolNode` and `SymbolEdge`.

## SymbolId Design (CRITICAL)
- Must be deterministic across runs.
- Components: `[ProjectName]:[RelativeFilePath]:[SymbolPath]`
- `SymbolPath` should include parent containers and member names (e.g., `Namespace.Class.Method`).
- Overloads: Include parameter types in the ID if necessary to disambiguate.

## SymbolKind Mapping
- `ts.SymbolFlags.Class` -> `SymbolKind.Type`
- `ts.SymbolFlags.Interface` -> `SymbolKind.Type`
- `ts.SymbolFlags.TypeAlias` -> `SymbolKind.Type` (or new 14)
- `ts.SymbolFlags.RegularEnum` -> `SymbolKind.Type`
- `ts.SymbolFlags.Function` -> `SymbolKind.Method`
- `ts.SymbolFlags.Method` -> `SymbolKind.Method`
- `ts.SymbolFlags.Property` -> `SymbolKind.Property`
- `ts.SymbolFlags.Module` -> `SymbolKind.Namespace`

## Documentation Extraction
- Use `checker.getSymbolDisplayBuilder().buildSymbolDisplay(symbol, ...)` or `symbol.getDocumentationComment(checker)`.
- Use `ts.getJSDocTags(node)` for structured tags.

## Pitfalls & Mitigations
- **Phantom Symbols**: Only process files in `program.getRootFileNames()`.
- **Memory Usage**: Cold-start process (already handled by Phase 28 IPC).
- **Circular References**: Use a `Set<ts.Symbol>` during traversal if needed, though the tree-walk usually avoids this.
