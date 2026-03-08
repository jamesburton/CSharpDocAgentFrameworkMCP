# Architecture Patterns

**Domain:** TypeScript language support integration into existing C# DocAgentFramework MCP server
**Researched:** 2026-03-08

## Recommended Architecture

### High-Level Integration Model: Node.js Sidecar with stdin/stdout NDJSON

The TypeScript sidecar runs as a child process spawned by the C# McpServer. Communication uses newline-delimited JSON (NDJSON) over stdin/stdout -- the same protocol pattern the MCP server itself uses with its clients. The sidecar is a **stateless request-response extractor**: C# sends a request ("extract symbols from this tsconfig.json"), the sidecar responds with a `SymbolGraphSnapshot`-shaped JSON payload, and the C# side deserializes it into the existing domain types.

```
                         ┌──────────────────────────────────────────────┐
                         │           DocAgent.McpServer (C#)            │
                         │                                              │
  MCP Client ──stdio──>  │  IngestionTools ──> TypeScriptIngestionSvc   │
                         │                         │                    │
                         │                    Process.Start()           │
                         │                    stdin/stdout NDJSON       │
                         │                         │                    │
                         │              ┌──────────v──────────┐        │
                         │              │  ts-symbol-extractor │        │
                         │              │   (Node.js sidecar)  │        │
                         │              │                      │        │
                         │              │  TypeScript Compiler │        │
                         │              │  API (ts.createProgram)│       │
                         │              └──────────────────────┘        │
                         │                         │                    │
                         │                    JSON response             │
                         │                         v                    │
                         │  SnapshotStore.SaveAsync(snapshot)           │
                         │  BM25SearchIndex.IndexAsync(snapshot)        │
                         │                                              │
                         │  -- All 14 existing MCP tools work --       │
                         └──────────────────────────────────────────────┘
```

### IPC Protocol Decision: Why stdin/stdout NDJSON

| Option | Verdict | Rationale |
|--------|---------|-----------|
| **stdin/stdout NDJSON** | **CHOSEN** | Zero network ports, zero dependencies beyond Node.js, matches the MCP stdio pattern already used by the server, simplest process lifecycle (kill child = cleanup done), works cross-platform, trivially testable with fixture JSON files |
| gRPC | Rejected | Requires protobuf tooling in both C# and Node.js, port management, health checks, proto file sync -- massive overhead for a same-machine request-response call |
| HTTP | Rejected | Requires port allocation, HTTP server in Node.js, HttpClient in C#, retry logic -- over-engineering for a subprocess |
| Named pipes | Rejected | Platform-specific names (Windows vs Unix), no advantage over stdin/stdout for a parent-child process pair, harder to test |

**Confidence: HIGH** -- stdin/stdout JSON is the established pattern for MCP tool communication and Language Server Protocol. The existing codebase already handles this pattern.

### Component Boundaries

| Component | Responsibility | Communicates With | New/Modified |
|-----------|---------------|-------------------|--------------|
| `ts-symbol-extractor/` | Node.js CLI: reads tsconfig.json, walks TS Compiler API, emits SymbolGraphSnapshot JSON | stdin/stdout with TypeScriptIngestionService | **NEW** (Node.js project) |
| `TypeScriptIngestionService` | C# service: spawns sidecar, sends request, deserializes response, feeds into SnapshotStore + SearchIndex | ts-symbol-extractor (child process), SnapshotStore, ISearchIndex | **NEW** (C# class in McpServer) |
| `TypeScriptTools` (or extend `IngestionTools`) | MCP tool handler for `ingest_typescript` | TypeScriptIngestionService, PathAllowlist | **NEW** (C# class in McpServer) |
| `SnapshotStore` | Persists SymbolGraphSnapshot artifacts (MessagePack) | TypeScriptIngestionService (caller) | **UNCHANGED** |
| `BM25SearchIndex` | Indexes snapshot for search | TypeScriptIngestionService (caller) | **UNCHANGED** |
| `DocTools`, `ChangeTools`, `SolutionTools` | Query/diff/review snapshots | KnowledgeQueryService, SymbolGraphDiffer | **UNCHANGED** |
| `SymbolGraphDiffer` | Static diff engine | Called by ChangeTools, SolutionTools | **UNCHANGED** |
| `DocAgent.AppHost/Program.cs` | Aspire orchestration | AddNodeApp for sidecar lifecycle | **MODIFIED** (add Node.js resource) |

### Data Flow: TypeScript Compiler API to SymbolGraphSnapshot

```
1. User calls `ingest_typescript` MCP tool with path to tsconfig.json
2. TypeScriptIngestionService validates path via PathAllowlist
3. TypeScriptIngestionService spawns `node ts-symbol-extractor/dist/index.js`
4. Sends NDJSON request on stdin:
   { "command": "extract", "tsconfigPath": "/abs/path/tsconfig.json" }
5. Sidecar:
   a. ts.createProgram() from tsconfig.json
   b. Walk all source files in the program
   c. For each file, walk the AST:
      - Modules/namespaces -> SymbolKind.Namespace
      - Interfaces -> SymbolKind.Type
      - Classes -> SymbolKind.Type
      - Functions -> SymbolKind.Method
      - Properties -> SymbolKind.Property
      - Enums -> SymbolKind.Type, members -> SymbolKind.EnumMember
      - Type aliases -> SymbolKind.Type
      - Constructors -> SymbolKind.Constructor
      - Accessors -> SymbolKind.Property
   d. Build edges: Contains, Inherits, Implements, References, Returns
   e. Extract JSDoc comments -> DocComment shape
   f. Compute source spans -> SourceSpan shape
   g. Emit JSON response matching SymbolGraphSnapshot schema on stdout
6. TypeScriptIngestionService deserializes JSON -> SymbolGraphSnapshot
7. Applies SymbolSorter for deterministic ordering (reuse existing C# logic)
8. SnapshotStore.SaveAsync() -> content-addressed .msgpack file
9. BM25SearchIndex.IndexAsync() -> search index updated
10. Return IngestionResult to MCP client
```

---

## IPC Protocol Specification

### Request (C# to Node.js, one NDJSON line on stdin)

```json
{
  "id": "req-1",
  "command": "extract",
  "tsconfigPath": "/absolute/path/to/tsconfig.json",
  "options": {
    "includePrivate": false,
    "includeNodeModules": false
  }
}
```

### Response (Node.js to C#, one NDJSON line on stdout)

```json
{
  "id": "req-1",
  "success": true,
  "snapshot": {
    "schemaVersion": "1.0",
    "projectName": "my-ts-project",
    "sourceFingerprint": "abc123...",
    "contentHash": null,
    "createdAt": "2026-03-08T12:00:00Z",
    "nodes": [ ... ],
    "edges": [ ... ]
  }
}
```

### Error Response

```json
{
  "id": "req-1",
  "success": false,
  "error": "Could not find tsconfig.json at /path/to/tsconfig.json"
}
```

### Protocol Design Decisions

- `id` field enables future request pipelining (not needed for v2.0 single-request pattern, but free to include)
- `contentHash` is null in the sidecar response -- C# computes it via SnapshotStore (matches existing Roslyn pattern where `RoslynSymbolGraphBuilder` returns `ContentHash: null`)
- Node SymbolIds use the same format: `{projectName}:{fullyQualifiedName}`
- Sidecar logs to **stderr only** (never stdout) to avoid corrupting the NDJSON stream
- Single newline terminates each JSON message (standard NDJSON)

---

## TypeScript Symbol Mapping

### SymbolKind Mapping

| TypeScript Concept | SymbolKind | SymbolId Format | Notes |
|-------------------|------------|-----------------|-------|
| Module (file-level) | `Namespace` | `proj:path/to/file` | TS modules are file-scoped |
| Namespace (explicit `namespace {}`) | `Namespace` | `proj:Ns.SubNs` | Rare in modern TS |
| Interface | `Type` | `proj:IFoo` or `proj:Ns.IFoo` | |
| Class | `Type` | `proj:MyClass` | |
| Type alias | `Type` | `proj:MyType` | Includes union, intersection, mapped, conditional |
| Enum | `Type` | `proj:MyEnum` | |
| Enum member | `EnumMember` | `proj:MyEnum.Value` | |
| Function (top-level) | `Method` | `proj:myFunction` | No `Function` kind in existing enum; Method is correct |
| Method (class/interface member) | `Method` | `proj:MyClass.myMethod` | |
| Property | `Property` | `proj:MyClass.myProp` | |
| Constructor | `Constructor` | `proj:MyClass.constructor` | |
| Getter/Setter | `Property` | `proj:MyClass.myProp` | Treated as property, not separate accessor |
| Type parameter | `TypeParameter` | `proj:MyClass.T` | |
| Parameter | `Parameter` | `proj:myFunction.paramName` | |

**Key decision:** The existing `SymbolKind` enum has 14 values. TypeScript symbols map naturally to 10 of them. No new enum values needed for v2.0.

### Edge Mapping

| TypeScript Relationship | SymbolEdgeKind | Example |
|------------------------|----------------|---------|
| Class extends Class | `Inherits` | `class B extends A` |
| Class implements Interface | `Implements` | `class C implements I` |
| Interface extends Interface | `Inherits` | `interface I2 extends I1` |
| Namespace contains Type | `Contains` | Module file contains class |
| Type contains Member | `Contains` | Class contains method |
| Method return type | `Returns` | Method returns a type |
| Method overrides base | `Overrides` | Override in derived class |
| Type reference in signature | `References` | Parameter type, property type |

### JSDoc to DocComment Mapping

| JSDoc Tag | DocComment Field | Notes |
|-----------|-----------------|-------|
| `@description` / first line | `Summary` | |
| `@remarks` | `Remarks` | |
| `@param name desc` | `Params["name"]` | |
| `@typeParam T desc` / `@template T desc` | `TypeParams["T"]` | |
| `@returns desc` | `Returns` | |
| `@example` | `Examples` list | |
| `@throws` / `@exception` | `Exceptions` list | |
| `@see` / `@link` | `SeeAlso` list | |

---

## Snapshot Storage: Same artifacts/ Directory

TypeScript snapshots use the **same** `artifacts/` directory and `manifest.json` as C# snapshots. Rationale:

1. **SnapshotStore is language-agnostic.** It stores `SymbolGraphSnapshot` objects. TS snapshots are `SymbolGraphSnapshot` objects. No changes needed.
2. **manifest.json already has `ProjectName`.** TS projects will have distinct names (e.g., "my-ts-app" vs "DocAgent.Core"), so no collision.
3. **Content-addressed storage prevents conflicts.** Each snapshot gets a unique XxHash128 filename.
4. **All query tools work unchanged.** `search_symbols`, `get_references`, `diff_snapshots` etc. operate on `SymbolGraphSnapshot` -- they do not know or care about the source language.

The only addition: an optional `LanguageOrigin` string property on `SymbolGraphSnapshot` (default null for backward compat with existing C# snapshots, "typescript" for TS snapshots). This is informational only -- it does not affect query, diff, or review behavior.

---

## Aspire Integration

The `DocAgent.AppHost/Program.cs` adds the Node.js sidecar as an Aspire resource using `Aspire.Hosting.NodeJs` (NuGet package):

```csharp
// Program.cs additions
var tsExtractor = builder.AddNodeApp("ts-extractor",
    scriptPath: "../ts-symbol-extractor/dist/index.js",
    workingDirectory: "../ts-symbol-extractor");

var mcpServer = builder.AddProject<Projects.DocAgent_McpServer>("docagent-mcp")
    .WithEnvironment("DOCAGENT_ARTIFACTS_DIR", ...)
    .WithEnvironment("DOCAGENT_TS_EXTRACTOR_PATH",
        "../ts-symbol-extractor/dist/index.js");
```

**Important caveat:** Aspire's `AddNodeApp` runs Node.js as a long-lived background process. For the sidecar pattern (spawned per-request or kept warm with stdin/stdout), the C# `TypeScriptIngestionService` manages the process directly via `System.Diagnostics.Process`. Aspire's role is limited to:
- Ensuring Node.js and npm are available in the dev environment
- Running `npm install` / `npm run build` during app host startup (via npm scripts)
- Dashboard visibility for the Node.js project
- Environment variable wiring

For v2.0: **C# spawns the sidecar on demand per ingestion request (cold start).** The ~200ms Node.js startup overhead is negligible compared to TypeScript compilation time (seconds to minutes). Warm process pooling is a v2.1 optimization if needed.

### Process Lifecycle

| Strategy | Pros | Cons | When |
|----------|------|------|------|
| **Cold start** (spawn per request) | Simplest, no state leak, clean isolation | ~200ms startup overhead | **v2.0 default** |
| **Warm pool** (long-lived process) | Fast repeated ingestions | Must handle crashes, stdin buffer management | v2.1 if latency matters |

---

## Patterns to Follow

### Pattern 1: PipelineOverride Seam (Already Established)

The existing `IngestionService` has a `PipelineOverride` property for testing without MSBuild. `TypeScriptIngestionService` must follow the same pattern:

```csharp
internal Func<string, CancellationToken, Task<SymbolGraphSnapshot>>? PipelineOverride { get; set; }
```

This allows tests to inject canned JSON responses without spawning Node.js.

### Pattern 2: Closure-Based Singleton Path Resolution

The existing `ServiceCollectionExtensions.AddDocAgent()` uses a closure to share the artifacts directory between `SnapshotStore` and `BM25SearchIndex`. TypeScript ingestion must use the same resolved path -- no separate artifacts directory.

### Pattern 3: PathAllowlist Before Pipeline Work

Every tool validates the path against `PathAllowlist` before doing any pipeline work. `ingest_typescript` must follow this pattern exactly -- check the tsconfig.json path before spawning the sidecar.

### Pattern 4: Deterministic Snapshot Output via SymbolSorter

The existing `SymbolSorter` sorts nodes and edges for deterministic output. The C# side should sort after deserialization using the existing `SymbolSorter`, rather than requiring the sidecar to sort. This keeps the sidecar simple and reuses proven sorting logic.

### Pattern 5: Content Hash Computed by SnapshotStore

The sidecar emits `contentHash: null`. The C# `SnapshotStore.SaveAsync()` computes the XxHash128 content hash. This matches the existing `RoslynSymbolGraphBuilder` pattern exactly.

### Pattern 6: Per-Path Semaphore Serialization

The existing `IngestionService` uses `ConcurrentDictionary<string, SemaphoreSlim>` to serialize same-project ingestions while allowing different projects in parallel. `TypeScriptIngestionService` should use the same pattern.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Adding Language-Specific Query Tools

**What:** Creating `search_typescript_symbols`, `get_typescript_symbol`, etc.
**Why bad:** Doubles the tool surface area. The existing 14 tools already work on any `SymbolGraphSnapshot`.
**Instead:** Use the existing tools. If a user wants only TS symbols, they use the `project` filter parameter on `search_symbols`.

### Anti-Pattern 2: Mixed-Language Snapshots

**What:** Putting C# and TypeScript symbols in the same `SymbolGraphSnapshot`.
**Why bad:** Breaks incremental ingestion, diffing, and content hashing. Cross-language edges are explicitly out of scope for v2.0.
**Instead:** Separate snapshots per language per project. Query tools can search across all loaded snapshots.

### Anti-Pattern 3: Sidecar as HTTP Server

**What:** Running the Node.js sidecar as a long-lived HTTP server with Express/Fastify.
**Why bad:** Port management, health checks, connection pooling, retry logic -- all unnecessary for a same-machine subprocess.
**Instead:** stdin/stdout NDJSON. The parent process owns the lifecycle.

### Anti-Pattern 4: Shared Code-Generated Types Between C# and Node.js

**What:** Generating C# classes from TypeScript interfaces (or vice versa) to keep them in sync.
**Why bad:** Build-time code generation adds toolchain complexity. The contract is simple JSON.
**Instead:** Define the JSON contract in a `PROTOCOL.md` document. Write a C# `TypeScriptExtractionResponse` record and a TypeScript `ExtractionResponse` interface. Test with fixture files that both sides validate.

### Anti-Pattern 5: Using ts-morph Instead of Raw Compiler API

**What:** Using ts-morph (wrapper library) instead of the TypeScript Compiler API directly.
**Why bad:** ts-morph adds ~4MB dependency and an abstraction layer over `ts.createProgram()`. The extraction logic is straightforward tree-walking -- the wrapper adds no value and makes it harder to control memory and performance.
**Instead:** Use `typescript` package directly: `ts.createProgram()`, `program.getTypeChecker()`, walk `ts.forEachChild()`.

---

## New Components (Build Order)

Ordered by dependency -- each step builds on the previous:

| Order | Component | Type | Depends On | Estimated Effort |
|-------|-----------|------|------------|-----------------|
| 1 | JSON contract types (`TypeScriptExtractionResponse`, etc.) | C# records in McpServer | Core domain types | Small |
| 2 | `ts-symbol-extractor/` Node.js project scaffold | New Node.js project | npm, typescript | Small |
| 3 | TS symbol walker (core extraction logic) | TypeScript code in sidecar | TypeScript Compiler API | **Large** -- core of the work |
| 4 | NDJSON stdin/stdout handler in sidecar | TypeScript code in sidecar | Step 3 | Small |
| 5 | `TypeScriptIngestionService` (C# process spawner + deserializer) | C# class in McpServer | Steps 1, 4 | Medium |
| 6 | `ingest_typescript` MCP tool | C# tool class in McpServer | Step 5, PathAllowlist | Small |
| 7 | Aspire AppHost wiring | C# modification to AppHost | Step 2 (npm build) | Small |
| 8 | Incremental TS ingestion (SHA-256 file hashing) | C# + TS | Steps 5, existing IncrementalIngestionEngine | Medium |

### Build Order Rationale

Steps 1-2 can run in parallel (C# contracts and Node.js scaffold are independent). Step 3 is the critical path -- the TS symbol walker is the most complex new code. Steps 5-6 are blocked on steps 3-4. Step 7 is independent of steps 3-6. Step 8 should come last because incremental ingestion requires the full pipeline to work first.

---

## Modified Components (Minimal Changes)

Only these existing files need changes:

| File | Change | Scope |
|------|--------|-------|
| `DocAgent.AppHost/Program.cs` | Add `Aspire.Hosting.NodeJs` reference and `AddNodeApp` call | 3-5 lines |
| `DocAgent.McpServer/ServiceCollectionExtensions.cs` | Register `TypeScriptIngestionService` and `ITypeScriptIngestionService` | 2-3 lines |
| `DocAgent.Core/Symbols.cs` (optional) | Add `LanguageOrigin` string property to `SymbolGraphSnapshot` (default null) | 1 line on record + MessagePack compat |
| `DocAgent.McpServer/Config/DocAgentServerOptions.cs` | Add `TypeScriptExtractorPath` config property | 1-2 lines |
| `src/Directory.Packages.props` | Add `Aspire.Hosting.NodeJs` package version | 1 line |
| `CLAUDE.md` | Document `ingest_typescript` tool | ~20 lines |

Everything else (SnapshotStore, BM25SearchIndex, DocTools, ChangeTools, SolutionTools, SymbolGraphDiffer, KnowledgeQueryService, PathAllowlist, AuditLogger, RateLimiter, all existing MCP tools) remains **completely unchanged**.

---

## Scalability Considerations

| Concern | At 1 project | At 10 projects (monorepo) | At 100+ files per project |
|---------|--------------|--------------------------|--------------------------|
| Sidecar startup | ~200ms cold start | Same (one process per ingestion call) | Same |
| Memory (Node.js) | ~50-100MB for TS compiler | ~200-500MB for large monorepo | Scales with file count |
| Memory (C#) | Negligible (JSON deserialization) | Same per-project pattern | Same |
| Snapshot size | Small (.msgpack) | One snapshot per TS project | Scales linearly with symbols |
| Index rebuild | Fast (BM25 is quick) | Per-project index | Same |

---

## Integration Points Summary

| Boundary | Current | v2.0 Change |
|----------|---------|-------------|
| `SnapshotStore` | Stores C# snapshots | Stores TS snapshots too (same API, no changes) |
| `BM25SearchIndex` | Indexes C# snapshots | Indexes TS snapshots too (same API, no changes) |
| `DocTools` (7 tools) | Queries snapshots | Queries TS snapshots identically (no changes) |
| `ChangeTools` (3 tools) | Diffs snapshots | Diffs TS snapshots identically (no changes) |
| `IngestionTools` | C# ingestion only | New `ingest_typescript` tool (new class or added to existing) |
| `PathAllowlist` | Validates .csproj/.sln paths | Also validates tsconfig.json paths (no code change needed) |
| `AuditFilter` | Audits all tool calls | Audits `ingest_typescript` calls automatically (no change) |
| `RateLimitFilter` | Rate limits all tools | Rate limits `ingest_typescript` automatically (no change) |
| `SymbolGraphDiffer` | Diffs C# snapshots | Diffs TS snapshots identically (static, no change) |

---

## Sources

- [Aspire AddNodeApp API reference](https://learn.microsoft.com/en-us/dotnet/api/aspire.hosting.nodeapphostingextension.addnodeapp?view=dotnet-aspire-9.0) -- MEDIUM confidence (API may have changed in later Aspire versions)
- [Aspire.Hosting.NodeJs NuGet package](https://www.nuget.org/packages/Aspire.Hosting.NodeJs) -- HIGH confidence
- [TypeScript Compiler API wiki](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API) -- HIGH confidence
- [ts-morph documentation](https://ts-morph.com/) -- HIGH confidence (evaluated and rejected)
- [C# to Node.js stdin/stdout IPC pattern](https://gist.github.com/elerch/5628117) -- MEDIUM confidence (community pattern)
- [IPC between .NET and Node.js](https://www.hardkoded.com/blog/interprocess-communication) -- MEDIUM confidence
- Existing codebase: `IngestionService.cs`, `SnapshotStore.cs`, `RoslynSymbolGraphBuilder.cs`, `Symbols.cs`, `Abstractions.cs`, `ServiceCollectionExtensions.cs`, `AppHost/Program.cs` -- HIGH confidence (primary source, direct reads)
