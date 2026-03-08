# Technology Stack: TypeScript Language Support (v2.0)

**Project:** DocAgentFramework - TypeScript Symbol Extraction via Node.js Sidecar
**Researched:** 2026-03-08
**Scope:** NEW additions only. Existing stack (Roslyn 4.14.0, Lucene.Net 4.8-beta, MessagePack 3.1.4, MCP SDK 1.0.0, Aspire SDK 13.1.2, OpenTelemetry 1.15.0, BenchmarkDotNet 0.15.8, etc.) is validated and unchanged.

---

## Recommended Stack Additions

### Node.js Sidecar Runtime

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Node.js | 24.x LTS (Krypton) | Runtime for TypeScript symbol extractor sidecar | Current Active LTS through Oct 2026, Maintenance through Apr 2028. Aspire 13.x has first-class Node.js orchestration via `AddNodeApp()`. |
| TypeScript | ~5.9.x | TypeScript Compiler API for symbol extraction | Latest stable (March 2026). Compiler API (`ts.createProgram`, `TypeChecker`, `Symbol`) provides compiler-grade symbol information -- identical fidelity to what Roslyn provides for C#. Pin to ~5.9.x to avoid TS 6.0 breaking changes (6.0 beta announced, will rewrite compiler internals). |

**Confidence:** HIGH -- Node.js 24 LTS and TypeScript 5.9 verified via official release channels and npm registry.

### Aspire Integration (C# Side)

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Aspire.Hosting.JavaScript | 13.1.2 | Orchestrate Node.js sidecar from AppHost | Renamed from `Aspire.Hosting.NodeJs` in Aspire 13.0. Old package is deprecated. Version 13.1.2 matches existing Aspire SDK already in the project. Provides `AddNodeApp(name, projectDir, scriptPath)` to register the sidecar as a managed Aspire resource with lifecycle management, health monitoring, environment variable injection, and dashboard visibility. |

**IMPORTANT:** Do NOT use `Aspire.Hosting.NodeJs` -- it is deprecated and no longer maintained. The replacement is `Aspire.Hosting.JavaScript` (same APIs, new package name).

**Confidence:** HIGH -- `Aspire.Hosting.JavaScript` 13.1.2 verified on NuGet, published 2026-02-26.

### IPC Protocol (C# <-> Node.js)

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| NDJSON over stdin/stdout | N/A (protocol, no dependency) | Bidirectional communication between C# host and Node.js sidecar | Proven pattern: the MCP server itself uses stdio transport. NDJSON (newline-delimited JSON) is trivial on both sides -- `System.Text.Json` in C#, `JSON.parse` per line in Node.js. Zero external dependencies. ~30ms latency per message via child process IPC. Request/response correlation via `id` field. |

**Why NOT alternatives:**

| Alternative | Why Rejected |
|-------------|-------------|
| HTTP/TCP | Port allocation, firewall issues, connection lifecycle management. Over-engineered for local parent-child process IPC. |
| gRPC | Proto file generation, build tool complexity. Symbol payloads are JSON-native. |
| Named pipes | Platform-specific behavior (Windows vs Linux). stdin/stdout is universal and already proven in this codebase. |
| StreamJsonRpc (Microsoft) | Good library but adds a NuGet dependency for a simple request/response pattern implementable in ~50 lines. Valid upgrade path if protocol complexity grows. |

**Confidence:** HIGH -- stdin/stdout NDJSON is the same pattern used by the existing MCP stdio transport.

---

## TypeScript Compiler API Surface (Node.js Side)

These are the specific APIs from the `typescript` npm package used for symbol extraction:

| API | Purpose | Maps To |
|-----|---------|---------|
| `ts.parseJsonConfigFileContent()` | Parse tsconfig.json including `extends` chains, `paths`, `include`/`exclude` globs | Project discovery (like MSBuildWorkspace for C#) |
| `ts.createProgram(rootNames, options, host)` | Create type-checked program from resolved file list | Equivalent to Roslyn `Compilation` |
| `program.getTypeChecker()` | Resolve types, symbols, and relationships | Equivalent to Roslyn `SemanticModel` |
| `program.getSourceFiles()` | Get all parsed source files | File enumeration for walking |
| `ts.forEachChild(node, visitor)` | Walk AST nodes to discover declarations | AST visitor pattern |
| `checker.getSymbolAtLocation(node.name)` | Get Symbol for a declaration node | Maps to `SymbolNode` in existing graph |
| `checker.getSignatureFromDeclaration(node)` | Extract function/method signatures (params, return type, generics) | Maps to `SymbolNode.Signature` |
| `checker.typeToString(type)` | Serialize resolved type to string | Display type for documentation |
| `ts.SyntaxKind` enum | Identify node types: `ClassDeclaration`, `InterfaceDeclaration`, `FunctionDeclaration`, `MethodDeclaration`, `PropertyDeclaration`, `EnumDeclaration`, `TypeAliasDeclaration`, `ModuleDeclaration`, `ExportDeclaration` | Maps to `SymbolKind` enum |
| `symbol.getJsDocTags()` / `symbol.getDocumentationComment()` | Extract JSDoc comments | Maps to `DocComment` in existing graph |

**Confidence:** HIGH -- TypeScript Compiler API is stable across major versions. Same core surface since TS 2.x with additive-only changes.

---

## npm Dependencies (Node.js Sidecar)

### Runtime Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `typescript` | ~5.9.x | TypeScript Compiler API -- the extraction engine |

**That is the ONLY runtime dependency.** stdin/stdout uses built-in `process.stdin`/`process.stdout`. JSON uses built-in `JSON.parse`/`JSON.stringify`. File system uses built-in `fs` and `path`. Zero npm dependencies beyond TypeScript itself.

### Dev Dependencies

| Package | Version | Purpose | Why This One |
|---------|---------|---------|-------------|
| `@types/node` | ^24 | Node.js type definitions for TypeScript | Match Node.js 24 LTS major version |
| `vitest` | ^3 | Test runner | Fast, zero-config, native ESM+TS support. Standard for new Node.js projects in 2026. |
| `tsx` | ^4 | TypeScript execution for development | Run .ts files directly without pre-compilation during dev |
| `esbuild` | ^0.25 | Bundler for production build | Produces single `dist/index.js`. Sub-second builds. Zero config. |

### What NOT to Add

| Package | Why Avoid |
|---------|-----------|
| `ts-morph` | Unnecessary abstraction for read-only extraction. +4MB dependency. Its value is code modification (refactoring), not read-only symbol walking. Direct Compiler API is cleaner. |
| `express` / `fastify` / any HTTP framework | Sidecar communicates via stdin/stdout, not HTTP. No web server needed. |
| `@grpc/grpc-js` / `@grpc/proto-loader` | gRPC is over-engineering for parent-child IPC. |
| `jest` | Heavier config, slower startup than vitest. Legacy choice. |
| `webpack` | Over-engineered bundler for a CLI tool. esbuild is faster and simpler. |
| `tree-sitter` / `tree-sitter-typescript` | Syntax-only parsing. No type resolution. Cannot extract resolved types, inferred return types, or generic instantiations. |
| `node-ipc` / `zeromq` / IPC libraries | stdin/stdout NDJSON requires zero dependencies and matches the existing MCP pattern. |

---

## NuGet Package Addition (C# Side)

| Package | Version | Target Project | Purpose |
|---------|---------|---------------|---------|
| `Aspire.Hosting.JavaScript` | 13.1.2 | `DocAgent.AppHost` only | `AddNodeApp()` extension method for Aspire resource registration |

**No other C# package additions needed.** The IPC layer uses:
- `System.Text.Json` -- built into .NET 10, already in dependency graph
- `System.Diagnostics.Process` -- BCL, already available
- `System.IO.Pipelines` -- built into .NET 10, for efficient stdin/stdout stream reading (optional optimization)

Existing `DocAgent.Core` types (`SymbolNode`, `SymbolEdge`, `SymbolGraphSnapshot`) are language-agnostic and accommodate TypeScript symbols without modification.

---

## Integration Architecture

### Data Flow

```
tsconfig.json
    |
    v
[Node.js Sidecar: DocAgent.TypeScriptExtractor]     <-- NEW project
    | (NDJSON over stdin/stdout)
    v
[DocAgent.Ingestion: TypeScriptIngestionService]     <-- NEW C# class
    | (deserialize JSON DTOs -> SymbolNode/SymbolEdge)
    v
[DocAgent.Core: SymbolGraphSnapshot]                 <-- EXISTING, unchanged
    |
    v
[DocAgent.Indexing: BM25SearchIndex]                 <-- EXISTING, unchanged
    |
    v
[DocAgent.McpServer: 15 MCP tools]                  <-- EXISTING 14 + new ingest_typescript
```

### Why Sidecar, Not Embedded JS

| Approach | Verdict | Rationale |
|----------|---------|-----------|
| Node.js sidecar process | **CHOSEN** | TypeScript Compiler API is designed for Node.js. Full `fs` access, V8 JIT optimization for type-checking workloads, trivial TS version upgrades (`npm update`), Aspire-native orchestration. |
| Jint (.NET JS interpreter) | Rejected | Cannot run the TypeScript compiler -- lacks full ES2020+ support, no `fs` module, inadequate performance for type-checking. |
| ClearScript / V8 embedding | Rejected | +50MB native dependency, custom `fs` shims needed, harder to debug than process isolation. |

### AppHost Integration

Current `Program.cs` (unchanged lines shown for context):

```csharp
// Add to DocAgent.AppHost/Program.cs:

// NEW: TypeScript symbol extractor sidecar
var tsExtractor = builder.AddNodeApp(
    "ts-extractor",
    "../DocAgent.TypeScriptExtractor",
    "dist/index.js")
    .WithEnvironment("NODE_ENV", "production");

// Existing MCP server gets reference to sidecar
mcpServer.WithReference(tsExtractor);
```

### IPC Message Protocol

Request (C# -> Node.js, one JSON object per line):
```json
{"id":"req-1","method":"extract","params":{"tsconfigPath":"/path/to/tsconfig.json"}}
```

Response (Node.js -> C#, one JSON object per line):
```json
{"id":"req-1","result":{"symbols":[...],"edges":[...],"fileHashes":{"src/index.ts":"sha256:abc..."}}}
```

Error:
```json
{"id":"req-1","error":{"code":-1,"message":"tsconfig.json not found at /path/to/tsconfig.json"}}
```

Correlation via `id` field. Simplified JSON-RPC 2.0 pattern (no full spec compliance needed).

---

## Node.js Sidecar Project Structure

```
src/DocAgent.TypeScriptExtractor/
  package.json
  tsconfig.json          # Sidecar's own build config (NOT target project config)
  src/
    index.ts             # NDJSON stdin/stdout entry point + message loop
    extractor.ts         # Core symbol extraction via TS Compiler API
    protocol.ts          # Request/response type definitions (mirrors C# DTOs)
    jsdoc.ts             # JSDoc comment extraction helpers
    symbol-mapper.ts     # Map TS Symbol -> SymbolNode JSON DTO
  test/
    extractor.test.ts    # Unit tests with fixture .ts projects
    protocol.test.ts     # NDJSON serialization round-trip tests
  fixtures/
    simple-project/      # Minimal tsconfig.json + .ts files for testing
  dist/
    index.js             # esbuild output (single bundled file)
```

### Sidecar tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2024",
    "module": "Node16",
    "moduleResolution": "Node16",
    "outDir": "dist",
    "rootDir": "src",
    "strict": true,
    "declaration": false,
    "sourceMap": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*.ts"]
}
```

### package.json

```json
{
  "name": "docagent-typescript-extractor",
  "version": "2.0.0",
  "type": "module",
  "main": "dist/index.js",
  "scripts": {
    "build": "esbuild src/index.ts --bundle --platform=node --outfile=dist/index.js --format=esm --external:typescript",
    "dev": "tsx src/index.ts",
    "test": "vitest run",
    "test:watch": "vitest"
  },
  "dependencies": {
    "typescript": "~5.9"
  },
  "devDependencies": {
    "@types/node": "^24",
    "vitest": "^3",
    "tsx": "^4",
    "esbuild": "^0.25"
  }
}
```

Note: `--external:typescript` in esbuild because the `typescript` package is large (~80MB) and should remain as a node_modules dependency, not bundled.

---

## Installation Commands

### C# Side

Add to root `Directory.Packages.props`:
```xml
<PackageVersion Include="Aspire.Hosting.JavaScript" Version="13.1.2" />
```

Add to `DocAgent.AppHost/DocAgent.AppHost.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.JavaScript" />
```

### Node.js Side

```bash
mkdir -p src/DocAgent.TypeScriptExtractor
cd src/DocAgent.TypeScriptExtractor
npm init -y
npm install typescript@"~5.9"
npm install -D @types/node@24 vitest@^3 tsx@^4 esbuild@^0.25
```

---

## Version Compatibility Matrix

| Component | Version | Constraint | Status |
|-----------|---------|------------|--------|
| Aspire SDK | 13.1.2 | Must match existing project | Already in use |
| Aspire.Hosting.JavaScript | 13.1.2 | Must match Aspire SDK | NEW -- verified on NuGet |
| Node.js | 24.x LTS | Aspire manages lifecycle | Active LTS through Oct 2026 |
| TypeScript | ~5.9.x | Pin minor; avoid TS 6.0 beta | Latest stable March 2026 |
| @types/node | ^24 | Match Node.js major | Dev dependency only |
| .NET | 10.0 | Existing constraint | Unchanged |
| System.Text.Json | Built-in | .NET 10 BCL | Already available |

---

## Sources

- [Aspire.Hosting.JavaScript 13.1.2 on NuGet](https://www.nuget.org/packages/Aspire.Hosting.JavaScript) -- published 2026-02-26 (HIGH confidence)
- [Aspire.Hosting.NodeJs deprecated on NuGet](https://www.nuget.org/packages/Aspire.Hosting.NodeJS) -- renamed to Aspire.Hosting.JavaScript in Aspire 13.0 (HIGH confidence)
- [Aspire JavaScript integration docs](https://aspire.dev/integrations/frameworks/javascript/) -- official docs, `AddNodeApp()` API (HIGH confidence)
- [Aspire Node.js extensions docs](https://aspire.dev/integrations/frameworks/nodejs-extensions/) -- community toolkit extensions (MEDIUM confidence)
- [TypeScript Compiler API wiki](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API) -- official Microsoft documentation (HIGH confidence)
- [TypeScript 5.9 release notes](https://www.typescriptlang.org/docs/handbook/release-notes/typescript-5-9.html) -- latest stable (HIGH confidence)
- [TypeScript 6.0 beta announcement](https://devblogs.microsoft.com/typescript/announcing-typescript-6-0-beta/) -- upcoming major, compiler rewrite (HIGH confidence)
- [Node.js release schedule](https://nodejs.org/en/about/previous-releases) -- Node 24 LTS "Krypton" (HIGH confidence)
- [NDJSON specification](https://github.com/ndjson/ndjson-spec) -- protocol spec (HIGH confidence)
- [Aspire 13 announcement](https://www.infoq.com/news/2025/11/dotnet-aspire-13-release/) -- polyglot platform rebranding (MEDIUM confidence)
- Existing codebase: `Directory.Packages.props`, `DocAgent.AppHost.csproj`, `AppHost/Program.cs` -- direct file reads (HIGH confidence)

---
*Stack research for: DocAgentFramework v2.0 TypeScript Language Support*
*Researched: 2026-03-08*
