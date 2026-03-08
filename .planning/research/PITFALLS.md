# Pitfalls Research

**Domain:** Adding TypeScript language support to an existing C#-based symbol graph analysis framework
**Researched:** 2026-03-08
**Confidence:** HIGH (compiler API behavior verified via official docs and issue trackers; Aspire integration verified via Microsoft docs; symbol model mapping based on direct codebase analysis of `RoslynSymbolGraphBuilder.GetSymbolId()` and `SymbolKind` enum)

---

## Critical Pitfalls

### Pitfall 1: SymbolId Scheme Mismatch -- No TypeScript Equivalent to GetDocumentationCommentId()

**What goes wrong:**
The existing C# pipeline constructs SymbolIds via Roslyn's `GetDocumentationCommentId()` (see `src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs:543`), producing ECMA-334 standard IDs like `T:MyNamespace.MyClass` and `M:MyNamespace.MyClass.Method(System.String)`. TypeScript has no equivalent convention. The TS compiler's `ts.Symbol.id` is a transient per-program integer that changes on every `createProgram()` call. `checker.getFullyQualifiedName()` returns inconsistent results across compiler versions and does not include parameter signatures. If the sidecar uses an unstable ID source, every re-ingestion produces a full "all removed + all added" diff.

**Why it happens:**
C# has a 20-year-old standard for documentation comment IDs. TypeScript was never designed for this use case. Developers reach for `ts.Symbol.id` or `getFullyQualifiedName()` without realizing these are session-scoped.

**How to avoid:**
Design a deterministic SymbolId construction function for TypeScript derived entirely from source-stable properties:
- File path relative to tsconfig root (forward-slash normalized)
- Fully qualified name built by walking the AST declaration chain (not the checker's resolved name)
- Parameter signature for functions/methods (parameter types as strings)
- Format: `T:src/models/User.User` or `M:src/services/auth.AuthService.login(string, string)`

Pin the TypeScript compiler version (`~5.7`, not `^5.7`) to prevent accidental changes in name resolution. Add a golden-file test that ingests a fixture project and asserts exact SymbolId values.

**Warning signs:**
- `diff_snapshots` on two identical ingestions shows changes (the killer signal)
- `get_references` returns empty for symbols that obviously have references
- Tests pass on first run but fail on re-ingestion

**Phase to address:**
Phase 1 (Symbol Extraction) -- this is the most critical design decision. Must be locked before any extraction code is written. Every downstream consumer (search, diff, change review) depends on ID stability.

---

### Pitfall 2: ts.createProgram() Cost -- 500MB-2GB Per Invocation

**What goes wrong:**
Each call to `ts.createProgram()` parses, binds, and (if checker is used) type-checks the entire project including all `.d.ts` files from `node_modules`. For a medium project (500 source files), this takes 5-15 seconds and consumes 500MB-2GB of memory. For monorepos or projects with heavy type dependencies, memory exceeds Node.js's default 1.5GB heap limit, producing `FATAL ERROR: Reached heap limit`.

**Why it happens:**
The TypeScript compiler loads the full type graph to resolve every symbol. Even with `skipLibCheck: true`, declaration files for dependencies are still parsed and bound. Developers unfamiliar with the compiler API call `createProgram` per-file or create new programs without releasing the old one.

**How to avoid:**
- Use **cold start** pattern for v2.0: spawn a new Node.js process per ingestion request. The OS reclaims all memory on exit. This is simpler and more reliable than managing long-lived programs.
- Create ONE `ts.Program` per ingestion and walk ALL source files from that single Program. Never per-file programs.
- Set `--max-old-space-size=4096` on the Node.js sidecar process.
- For future incremental support (v2.1+), use `ts.createIncrementalProgram` with a `ts.BuilderProgram` and the `oldProgram` parameter.
- Profile memory with a real-world large project (e.g., TypeScript compiler source: ~2,600 files) during development.

**Warning signs:**
- Sidecar crashes with exit code 134 (SIGABRT / OOM) on projects with >200 source files
- C# side sees "process exited unexpectedly" with no diagnostic
- Ingestion takes >30 seconds for a small project
- Memory does not decrease between successive ingestion requests (warm process pattern)

**Phase to address:**
Phase 1 (Symbol Extraction) -- the cold-start architecture must be the default from day one.

---

### Pitfall 3: .d.ts Declaration Files Creating Phantom Symbols

**What goes wrong:**
`ts.Program.getSourceFiles()` returns EVERYTHING the compiler loaded: your `.ts` source files, `.d.ts` declaration files from `node_modules`, and `lib.d.ts` (DOM/Node built-ins). Walking all returned source files indiscriminately indexes thousands of external library symbols (`Buffer`, `Promise`, `Array`, `HTMLElement`, every Express type, etc.) as if they belong to the project. This bloats snapshots by 10-100x, pollutes search results, and creates false diffs when dependency versions change.

**Why it happens:**
`getSourceFiles()` returns everything the compiler needed to type-check, not just "your code." The API provides no built-in filter for "project vs external." Developers assume `getSourceFiles()` means "files the user wrote."

**How to avoid:**
- Filter source files to ONLY those in `ts.Program.getRootFileNames()` (the files listed in `tsconfig.json`'s `include`/`files`).
- Additionally check `sourceFile.isDeclarationFile` to skip any `.d.ts` file.
- For referenced types from external packages, create stub nodes (`NodeKind.Stub`) exactly as the existing C# pipeline does for NuGet references -- capped to direct `dependencies` from `package.json`, not transitive.
- Always filter out files whose path includes `node_modules/`, regardless of tsconfig settings.

**Warning signs:**
- Snapshot contains thousands of symbols named `Buffer`, `Promise`, `Array`
- Search for a user-defined type returns hits from library types
- Snapshot file size is megabytes instead of kilobytes
- Ingestion takes minutes instead of seconds

**Phase to address:**
Phase 1 (Symbol Extraction) -- file filtering must be baked into the initial walker.

---

### Pitfall 4: TypeScript Module System Does Not Map to Namespaces

**What goes wrong:**
The existing symbol graph uses `SymbolKind.Namespace` nodes as the primary containment hierarchy, with `SymbolEdgeKind.Contains` edges connecting namespaces to types. TypeScript uses modules (files) as the containment unit. A TypeScript file can export multiple classes, functions, and type aliases with no namespace wrapper. If you either (a) create a fake namespace per file (confusing users) or (b) flatten everything to top-level (losing hierarchy), the MCP tools produce confusing results.

**Why it happens:**
The containment model was designed for C#'s `namespace { class { method } }` hierarchy. TypeScript's `namespace` keyword exists but is rarely used in modern ESM code. The module IS the organizational unit.

**How to avoid:**
Map TypeScript modules to `SymbolKind.Namespace` nodes using the module's path relative to the tsconfig root (e.g., `src/services/auth`). This preserves `Contains` edges and navigable hierarchy while being semantically honest. Document in MCP tool descriptions that for TypeScript projects, "namespace" represents the module file path. Do NOT synthesize namespaces from directory structure or export groupings -- use the actual file as the boundary.

**Warning signs:**
- `explain_project` shows a flat list of hundreds of symbols with no hierarchy
- `search_symbols` with `kindFilter=Namespace` returns nothing for TypeScript
- Users cannot navigate the containment graph to browse a TypeScript codebase

**Phase to address:**
Phase 1 (Symbol Extraction) -- determines the entire graph structure. Must be decided before the walker is built.

---

### Pitfall 5: Sidecar stdout Pollution Corrupts IPC Protocol

**What goes wrong:**
The Node.js sidecar communicates with the C# host via stdout (NDJSON or MessagePack). Any stray `console.log()`, library warning, or Node.js deprecation notice written to stdout corrupts the message stream. The C# deserializer throws `JsonException` or `MessagePackSerializationException`. The ingestion appears to hang or crash with no useful diagnostic.

**Why it happens:**
JavaScript developers habitually use `console.log()` for debugging. Libraries (including TypeScript) may emit warnings to stdout. Node.js runtime deprecation warnings go to stderr by default but some configurations redirect them.

**How to avoid:**
- Route ALL logging/diagnostics to `process.stderr` -- never `process.stdout`
- Set `--no-warnings` Node.js flag when spawning the sidecar
- In the C# host, capture stderr separately and log as warnings via `ILogger`
- Add a protocol integration test that sends a valid request and asserts the response is valid (catches any stdout pollution immediately)
- Wrap the sidecar's entry point in a try/catch that writes errors to stderr, never stdout

**Warning signs:**
- Ingestion fails with opaque JSON/MessagePack parse error
- Adding a debugging `console.log` breaks ingestion
- Upgrading a dependency introduces new stdout warnings

**Phase to address:**
Phase 1 (Symbol Extraction) -- the protocol discipline must be established from the first line of sidecar code.

---

### Pitfall 6: SymbolKind Enum Mapping Gaps Between C# and TypeScript

**What goes wrong:**
The existing `SymbolKind` enum (14 values in `src/DocAgent.Core/Symbols.cs`) was designed for C#. TypeScript has concepts that do not map cleanly: type aliases, union/intersection types, interfaces (always public), module-level functions (no containing class), `const`/`let` exports, re-exports, barrel files (`index.ts` that re-exports everything). Mapping everything to `SymbolKind.Type` loses semantic information. Adding TypeScript-specific enum values breaks existing consumers that exhaustively switch on `SymbolKind`.

**Why it happens:**
Language-specific assumptions baked into the enum. PROJECT.md says "natural mappings first; gaps tracked" but does not specify what "natural" means for each TypeScript construct.

**How to avoid:**
Define the complete mapping table BEFORE writing extraction code:

| TypeScript Concept | Maps To | Notes |
|---|---|---|
| `interface` | `Type` | Accessibility = Public always |
| `class` | `Type` | Direct match |
| `type alias` | `Type` | Distinguish via FQN convention or future metadata |
| `enum` | `Type` | Members as `EnumMember` |
| `enum member` | `EnumMember` | Direct match |
| `function` (module-level) | `Method` | Parent is module/namespace node |
| `const`/`let`/`var` export | `Field` | Or `Property` for object-shaped values |
| `constructor` | `Constructor` | Direct match |
| `method` | `Method` | Direct match |
| `property` | `Property` | Direct match |
| `get`/`set` accessor | `Property` | Merge getter+setter into one node |
| `index signature` | `Indexer` | Direct match |
| `type parameter` | `TypeParameter` | Direct match |
| `parameter` | `Parameter` | Direct match |
| `re-export / barrel` | Skip | Follow to original source, do not create duplicate nodes |

Do NOT add new `SymbolKind` values in v2.0 -- track gaps for v3.0. The mapping table is a design artifact, not code.

**Warning signs:**
- All TypeScript symbols show as `SymbolKind.Type` regardless of what they actually are
- `search_symbols` with `kindFilter=Method` misses module-level functions
- The mapping is decided ad-hoc during implementation instead of up front

**Phase to address:**
Phase 1 (Symbol Extraction) -- design decision that must be documented and reviewed before coding starts.

---

### Pitfall 7: IPC Serialization Overhead for Large Codebases

**What goes wrong:**
The sidecar must send extracted symbols to the C# host. For a large codebase (2,000+ source files), the graph can contain 50,000+ symbols with edges. JSON serialization produces multi-megabyte payloads (10-50MB) that are slow to serialize in Node.js, slow to transmit over stdio, and slow to deserialize in C#. Without length-prefixed framing, partial reads cause deserialization failures.

**Why it happens:**
JSON is the path of least resistance for Node.js-to-C# communication. Developers prototype with small projects (10-50 files) and never encounter the scaling cliff until integration testing.

**How to avoid:**
- Use NDJSON (newline-delimited JSON) with streaming deserialization from Phase 1. Each line is one complete JSON object.
- Design the protocol to support chunked responses: send symbols in batches (e.g., 500 per message) rather than one giant blob.
- For Phase 2+, consider MessagePack (`@msgpack/msgpack` on Node side) to match the existing C# MessagePack serialization. Test round-trip compatibility between implementations -- they handle timestamps and extension types differently.
- Profile with a real large project early (not just 10-file test fixtures).

**Warning signs:**
- Ingestion works for test fixtures but times out on real projects
- IPC messages are truncated (partial JSON parse errors on C# side)
- Ingestion latency is dominated by serialization time, not compilation time

**Phase to address:**
Phase 2 (IPC Protocol) -- the serialization format must be chosen before building the IPC layer.

---

### Pitfall 8: Path Resolution (baseUrl, paths, rootDirs) Creating Duplicate or Missing Symbols

**What goes wrong:**
TypeScript projects commonly use `baseUrl`, `paths`, and `rootDirs` in `tsconfig.json` to create import aliases (`import { User } from '@/models/user'` maps to `src/models/user.ts`). If the sidecar uses import specifiers instead of resolved file paths when constructing SymbolIds, the same file gets indexed under both its real path and its aliased path, producing duplicate symbols. Or aliased imports produce unresolved references.

**Why it happens:**
The TypeScript compiler resolves `paths` internally during program creation. When walking the AST, import declarations contain the alias string (`@/models/user`), not the resolved path. The compiler does not expose a trivial "canonical path" API -- you must use `sourceFile.fileName` (which IS the resolved absolute path) or call `ts.resolveModuleName` manually.

**How to avoid:**
- Always use `sourceFile.fileName` (the resolved absolute path) as the canonical file identifier for SymbolId construction. Never use import specifiers.
- Normalize all paths to forward slashes and relative-to-tsconfig-root before constructing SymbolIds.
- Use `ts.sys.realpath` to resolve symlinks (common in monorepo `node_modules` with npm/yarn/pnpm workspaces).
- Test with a project that uses `paths` aliases extensively.

**Warning signs:**
- Duplicate symbols in the snapshot with different IDs pointing to the same source
- `get_references` misses references that use path aliases
- Some files appear twice in `explain_project` output

**Phase to address:**
Phase 1 (Symbol Extraction) -- path normalization affects SymbolId construction and must be built into the walker from the start.

---

### Pitfall 9: TSDoc vs JSDoc Parsing -- Two Formats, One DocComment Target

**What goes wrong:**
TypeScript codebases mix JSDoc (`/** @param {string} name */`) and TSDoc (`/** @param name - description */`) freely. JSDoc puts types in `{braces}` (redundant in TS), TSDoc uses `@inheritDoc` (capital D) vs JSDoc's `@inheritdoc`, JSDoc's `@example` implies a code block while TSDoc requires explicit fencing. The TypeScript compiler itself does NOT parse doc comments into structured data -- it only returns raw comment text via `symbol.getDocumentationComment()`. A custom parser built for one format silently misparses the other.

**Why it happens:**
TSDoc was designed to replace JSDoc for TypeScript but adoption is incomplete. Most real codebases mix both styles, sometimes in the same file. Developers build a parser for whichever style they encounter first and miss the other.

**How to avoid:**
- Use the `@microsoft/tsdoc` package for parsing. It handles both TSDoc and JSDoc-style comments with graceful degradation.
- Map parsed output to the existing `DocComment` record (`Summary`, `Remarks`, `Params`, `Returns`, `Examples`, `Exceptions`, `SeeAlso`).
- Strip `{type}` annotations from JSDoc `@param` and `@returns` tags (redundant with TypeScript's type system).
- Test against real-world projects using both styles: Express.js (JSDoc), Angular (TSDoc-like).

**Warning signs:**
- `get_doc_coverage` reports 0% on a well-documented TypeScript project
- `get_symbol` shows raw comment text instead of structured documentation
- Parameter descriptions contain `{string}` type annotations as prose text

**Phase to address:**
Phase 1-2 (Doc Parsing) -- can follow symbol extraction but must be done before the milestone ships.

---

### Pitfall 10: Node.js Sidecar Lifecycle Not Managed by Aspire

**What goes wrong:**
Aspire's `AddNpmApp` / `AddNodeApp` starts the sidecar but provides no automatic restart, health checking, or graceful shutdown. If the sidecar exits (OOM, unhandled rejection, signal), the C# host has no notification beyond `Process.HasExited`. The MCP server accepts `ingest_typescript` calls that hang indefinitely because the sidecar is dead. Aspire dashboard may still show the resource as "running."

**Why it happens:**
Aspire is designed for HTTP services with health endpoints (`/health`, `/ready`). A stdio/IPC-based compiler worker is an unusual pattern. Most Aspire Node.js examples are Express or Vite servers.

**How to avoid:**
- For v2.0 cold-start pattern: spawn a new process per ingestion request. Check `Process.ExitCode` after completion. Timeout with `CancellationToken` (separate `TypeScriptIngestionTimeoutSeconds` config, default 600s).
- For future warm-process pattern: send periodic ping messages over IPC; restart on timeout.
- Handle `SIGTERM`/`SIGINT` in the sidecar to flush in-progress work.
- Set `--max-old-space-size` in the Aspire resource configuration.
- Add OpenTelemetry spans to the sidecar for Aspire dashboard visibility.

**Warning signs:**
- `ingest_typescript` hangs indefinitely
- Sidecar is not running but no error is logged
- Memory climbs continuously during repeated ingestions
- Exit code is non-zero but error details are lost

**Phase to address:**
Phase 2-3 (Integration) -- basic process management is needed for Phase 1 development, but robust lifecycle management can follow.

---

### Pitfall 11: Node.js Not Found on PATH at Runtime

**What goes wrong:**
`Process.Start("node", ...)` fails with `Win32Exception: The system cannot find the file specified` when Node.js is not on the system PATH. This happens in CI containers, Docker images, nvm-managed installations (user-specific paths), and Aspire-managed environments that do not propagate PATH.

**Why it happens:**
Node.js installation varies by platform. The C# host assumes `node` is globally available, which is not guaranteed.

**How to avoid:**
- Add `TypeScriptExtractorNodePath` to `DocAgentServerOptions` (default: `"node"` for PATH lookup).
- In `StartupValidator`, check the configured path is executable and meets minimum version (Node.js 22+).
- Return a structured error: `"Node.js not found. Install Node.js 22+ or set DOCAGENT_NODE_PATH."` -- not a raw `Win32Exception`.
- In Aspire AppHost, use `WithEnvironment("PATH", ...)` to ensure discoverability.

**Warning signs:**
- `ingest_typescript` fails immediately with a system-level exception
- Works on developer machines but fails in CI

**Phase to address:**
Phase 1 (Sidecar Scaffold) -- startup validation must check Node.js availability.

---

### Pitfall 12: Monorepo Project References -- Cycles and Redundant Ingestion

**What goes wrong:**
TypeScript monorepos use `references` in `tsconfig.json` to declare inter-project dependencies. Naively following references during ingestion causes: (a) the same package ingested multiple times, (b) circular references causing infinite loops (TS project references CAN form cycles, unlike MSBuild which is always a DAG), and (c) symbols from referenced projects appearing as both `Real` and `Stub` nodes.

**Why it happens:**
The C# solution ingestion assumes MSBuild's acyclic dependency graph. TypeScript project references can form cycles in practice. The ingestion pipeline does not have cycle detection.

**How to avoid:**
- Treat each `tsconfig.json` as an independent project (like each `.csproj`).
- Do NOT follow `references` during single-project ingestion.
- For monorepo ingestion (future phase), build a dependency graph from `references`, detect and error on cycles, topologically sort.
- Use the existing cross-project edge pattern (`EdgeScope.CrossProject`, stub nodes) for referenced project types.

**Warning signs:**
- Monorepo ingestion takes 10x longer than expected
- Same symbol appears multiple times with different IDs
- Ingestion hangs on circular references

**Phase to address:**
Phase 3+ (Monorepo) -- single-project ingestion should ignore references entirely.

---

### Pitfall 13: TypeScript 7 Go Rewrite Will Break the Compiler API

**What goes wrong:**
Microsoft is rewriting the TypeScript compiler in Go ("Project Corsa"). TypeScript 7.0 ships the Go compiler; TypeScript 6.x continues the JavaScript compiler. The JavaScript Compiler API (`ts.createProgram`, `ts.TypeChecker`) may become async in 7.0 or be replaced entirely. A sidecar built against the current synchronous API will need rework when 7.0 ships (expected mid-to-late 2026).

**Why it happens:**
The Go rewrite fundamentally changes the compiler architecture. The JavaScript API is maintained for backward compatibility in 6.x, but the long-term direction is the native compiler with a new API surface.

**How to avoid:**
- Build against TypeScript 5.x (current stable). Pin in `package.json` with `~5.7`.
- Abstract the compiler interaction behind an interface within the sidecar: `ISymbolExtractor` with methods like `extractSymbols(configPath)` that hide the TS API surface.
- When TypeScript 7 ships, the abstraction layer limits the blast radius to one module.
- Do NOT use TypeScript compiler internals (non-public APIs) -- they will definitely break.

**Warning signs:**
- TypeScript 7 preview breaks the sidecar without code changes
- Build CI fails when TypeScript auto-updates
- Using internal compiler APIs that are documented as unstable

**Phase to address:**
Phase 1 (Symbol Extraction) -- pin version and add abstraction layer from the start. Defensive architecture decision.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| JSON over stdio instead of MessagePack for IPC | Faster to prototype, easy to debug with `jq` | 3-5x slower for large projects, memory pressure | Phase 1 prototype only; migrate before milestone completion |
| Cold-start (new process per ingestion) instead of warm sidecar | No memory management, no lifecycle complexity | Extra 2-3s startup per ingestion request | Acceptable for v2.0; optimize in v2.1 if needed |
| Skipping incremental compilation | Simpler sidecar code, no state management | Full re-ingestion every time, even for single-file changes | Phase 1 acceptable; incremental needed before v2.0 ships |
| Hardcoding SymbolKind mapping without distinguishing metadata | Fewer fields to serialize | Cannot distinguish interface from class from type alias | Never -- the mapping table must be explicit from Phase 1 |
| Not handling `export default` specially | Fewer edge cases | Default exports produce cryptic SymbolIds like `M:module.default` | Phase 1 acceptable if documented; improve in Phase 2 |
| Ignoring `paths`/`baseUrl` aliases | Works for projects without aliases | Silent symbol loss or duplication | Never -- always use resolved paths |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Node.js sidecar via Aspire | Using `AddNpmApp` for a worker process that does not serve HTTP | Use `AddNodeApp` or custom executable resource; health checks via IPC, not HTTP |
| MessagePack C# to Node.js | Assuming `MessagePack-CSharp` and `@msgpack/msgpack` are wire-compatible | Test round-trip serialization of actual records; timestamp and extension type formats differ |
| TypeScript Compiler API | Calling `ts.createProgram` per source file | Create ONE Program for the entire project; walk all source files from that single instance |
| SymbolGraphSnapshot | Merging TS symbols into the same snapshot as C# symbols | Separate snapshots per language (per PROJECT.md scope); no cross-language edges in v2.0 |
| BM25 Search Index | Reusing the CamelCase tokenizer unmodified | TypeScript uses camelCase (not PascalCase). Verify `camelCaseFunction` tokenizes to `["camel", "case", "function"]` |
| PathAllowlist | Sidecar reads any path | Sidecar must respect the same PathAllowlist; pass allowed paths as startup configuration |
| C# JSON deserialization | Default case-sensitive `System.Text.Json` | Use `PropertyNameCaseInsensitive = true` or `JsonPropertyName` attributes to match camelCase from Node.js |
| tsconfig.json parsing | Reading the file directly with `JSON.parse()` | Use `ts.readConfigFile()` + `ts.parseJsonConfigFileContent()` to resolve `extends`, `references`, and compiler defaults |
| esbuild bundling | Bundling the `typescript` package into the output | Mark `typescript` as `--external:typescript`; the 5MB TS compiler should be a runtime `node_modules` dependency |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| New `ts.Program` without releasing old one | Memory climbs 500MB+ per ingestion | Cold-start pattern (new process per request) or null out references | After 3-5 ingestions |
| Walking all source files including `.d.ts` | 60+ second ingestion, 100K+ symbol snapshots | Filter to `getRootFileNames()`, check `isDeclarationFile` | Any project with >5 dependencies |
| JSON serialization of large symbol graphs | 30+ second parse time for 50K symbols, OOM | NDJSON streaming or MessagePack batching | Projects with >1,000 source files |
| Synchronous file I/O in Node.js sidecar | Event loop blocks, health pings time out | Use `fs/promises` and async iteration | Projects with >500 files |
| Not caching parsed tsconfig.json | Re-parsing every ingestion request | Cache keyed on file mtime | Monorepos with 10+ tsconfig files |
| Full type-checking when only symbol extraction is needed | 2-3x slower than needed | Evaluate whether `noCheck: true` is viable (checker still needed for type resolution) | All projects; but may not be viable |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Sidecar reads files outside PathAllowlist | Directory traversal; leaks unrelated source code | Pass AllowedPaths to sidecar at startup; validate every file read |
| Sidecar executes tsconfig.json compiler plugins | Arbitrary code execution via malicious tsconfig | Disable TS compiler plugins; do not use `ts.createSolutionBuilder` which runs transforms |
| IPC channel on TCP socket or named pipe | Symbol data accessible to other processes | Use stdin/stdout inherited from Aspire, not network sockets |
| Absolute file paths in SymbolNode.Span | Leaks server filesystem structure to MCP clients | Normalize to paths relative to project root before storage |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| TS SymbolIds use different format than C# SymbolIds | Users must learn two conventions for `get_symbol` | Use a consistent prefix scheme or document the format per-language in tool descriptions |
| Mixed C# and TS results with no language indicator | Users cannot tell which language a search result comes from | Searches are scoped to per-language snapshots; make this clear in tool descriptions |
| `ingest_typescript` accepts tsconfig.json path but user passes a directory | Cryptic error about missing file | Accept both directory (auto-find tsconfig.json) and explicit file; suggest the right path in error |
| Module-level functions without a "class" parent | C#-oriented users confused by functions outside classes | Document in `explain_project` that TS modules are shown as namespace-like containers |
| `get_doc_coverage` counts TS coverage differently | All interfaces are "public" in TS; Accessibility filter meaningless | Adjust coverage rules for TypeScript: all exported symbols count as public |

## "Looks Done But Isn't" Checklist

- [ ] **Re-exports:** Verify `export { Foo } from './bar'` creates the correct SymbolId pointing to the original declaration, not a duplicate node
- [ ] **Default exports:** Verify `export default class MyClass` gets a meaningful SymbolId (not `M:module.default`)
- [ ] **Documentation parsing:** Verify multi-line `@example` blocks are captured in `DocComment.Examples`
- [ ] **Incremental ingestion:** Verify renaming a file and re-ingesting shows old symbols removed and new ones added (not duplicates)
- [ ] **Diff determinism:** Verify `diff_snapshots` on two identical ingestions produces ZERO changes (the critical stability test)
- [ ] **Search tokenization:** Verify `searchSymbols("auth")` finds a function named `authenticateUser` (camelCase tokenization)
- [ ] **Cross-project edges:** Verify a type imported from a monorepo sibling package shows a `CrossProject` edge
- [ ] **Path security:** Verify sidecar rejects file reads outside PathAllowlist
- [ ] **Sidecar crash recovery:** Verify killing the sidecar mid-ingestion produces a clear error within 10 seconds, not a hang
- [ ] **Determinism:** Verify two ingestions of the same project at the same commit produce byte-identical snapshots (MessagePack content hash match)
- [ ] **Path aliases:** Verify a project using `paths` aliases has zero duplicate symbols
- [ ] **SchemaVersion:** Verify v2.0 snapshots have `SchemaVersion = "2.0"` and v1.5 server warns on load

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Unstable SymbolIds | HIGH | Redesign ID scheme; re-ingest all TS projects; invalidate cached snapshots/diffs |
| Memory leaks in sidecar | LOW | Switch to cold-start, add `--max-old-space-size`, restart on OOM |
| .d.ts bloat in snapshots | MEDIUM | Add source file filter; re-ingest; old snapshots valid but oversized |
| Wrong namespace mapping | HIGH | Restructure graph hierarchy; breaks all TS snapshots and diffs |
| TSDoc/JSDoc parse failures | LOW | Swap parser library; re-ingest; symbol structure unaffected |
| Sidecar lifecycle crashes | LOW | Add restart logic and health checks; ingestion is idempotent |
| IPC serialization bottleneck | MEDIUM | Switch from JSON to MessagePack/NDJSON; changes on both sides |
| Path resolution bugs | MEDIUM | Fix normalization; re-ingest; may invalidate snapshots with bad IDs |
| SymbolKind mapping confusion | HIGH | Changing the mapping after users depend on it breaks filters and clients |
| Circular monorepo references | LOW | Add cycle detection; does not affect single-project ingestion |
| TypeScript 7 API breakage | MEDIUM | Abstraction layer limits blast radius to one module; still requires rework |
| Node.js not found | LOW | Add startup validation; actionable error message |
| stdout pollution | LOW | Route all logging to stderr; add protocol test |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| SymbolId scheme mismatch | Phase 1 (Symbol Extraction) | `diff_snapshots` on two identical ingestions produces zero changes |
| ts.createProgram cost | Phase 1 (Symbol Extraction) | 500-file project ingests in <30s with <2GB memory |
| .d.ts phantom symbols | Phase 1 (Symbol Extraction) | Snapshot contains only project source + capped stub nodes |
| Module-to-namespace mapping | Phase 1 (Symbol Extraction) | `explain_project` shows hierarchical structure |
| stdout pollution | Phase 1 (Sidecar Scaffold) | Protocol integration test passes |
| SymbolKind mapping | Phase 1 (Symbol Extraction) | Mapping table documented and reviewed before coding |
| Path resolution | Phase 1 (Symbol Extraction) | Project with `paths` aliases has zero duplicates |
| TypeScript 7 risk | Phase 1 (Symbol Extraction) | TS version pinned; compiler interaction behind abstraction |
| Node.js not found | Phase 1 (Sidecar Scaffold) | Startup validator checks Node.js availability |
| TSDoc/JSDoc parsing | Phase 1-2 (Doc Parsing) | `get_doc_coverage` >0% for projects with either format |
| IPC serialization | Phase 2 (IPC Protocol) | 2,000-file project ingests without timeout or OOM |
| Sidecar lifecycle | Phase 2-3 (Integration) | Sidecar crash produces error within 10s, restarts |
| Monorepo references | Phase 3+ (Monorepo) | Circular config errors; no duplicate symbols |

---

## Sources

- [TypeScript Wiki: Using the Compiler API](https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API) -- official compiler API documentation (HIGH confidence)
- [TypeScript Wiki: Performance](https://github.com/microsoft/Typescript/wiki/Performance) -- memory and compilation guidance (HIGH confidence)
- [TypeScript Issue #10759: Very high memory usage](https://github.com/microsoft/TypeScript/issues/10759) -- memory consumption patterns (HIGH confidence)
- [TypeScript Issue #62543: Performance Enhancement Opportunities](https://github.com/microsoft/typescript/issues/62543) -- caching and optimization (MEDIUM confidence)
- [TypeScript Issue #25338: .d.ts duplicate identifier errors in build mode](https://github.com/microsoft/TypeScript/issues/25338) -- declaration file duplication (HIGH confidence)
- [TSDoc Specification](https://tsdoc.org/) -- TSDoc vs JSDoc differences (HIGH confidence)
- [TypeScript TSConfig Reference: paths](https://www.typescriptlang.org/tsconfig/paths.html) -- path alias resolution (HIGH confidence)
- [TypeScript: Project References](https://www.typescriptlang.org/docs/handbook/project-references.html) -- monorepo project references (HIGH confidence)
- [Aspire: Orchestrate Node.js apps](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/build-aspire-apps-with-nodejs) -- Node.js lifecycle in Aspire (HIGH confidence)
- [TypeScript Native Port announcement](https://devblogs.microsoft.com/typescript/typescript-native-port/) -- TypeScript 7 Go rewrite impact (HIGH confidence)
- [TypeScript 5.x to 6.0 Migration Guide](https://gist.github.com/privatenumber/3d2e80da28f84ee30b77d53e1693378f) -- API stability risks (MEDIUM confidence)
- Direct codebase: `RoslynSymbolGraphBuilder.GetSymbolId()` at `src/DocAgent.Ingestion/RoslynSymbolGraphBuilder.cs:541-544` -- C# SymbolId uses `GetDocumentationCommentId()` (HIGH confidence)
- Direct codebase: `SymbolKind` enum at `src/DocAgent.Core/Symbols.cs:3-19` -- 14 values designed for C# (HIGH confidence)
- Direct codebase: `NodeKind`, `EdgeScope` enums at `src/DocAgent.Core/Symbols.cs:61-78` -- stub node and cross-project edge patterns (HIGH confidence)

---
*Pitfalls research for: DocAgentFramework v2.0 TypeScript Language Support*
*Researched: 2026-03-08*
