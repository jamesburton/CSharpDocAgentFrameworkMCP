# Pitfalls Research

**Domain:** .NET code documentation agent framework with Roslyn symbol extraction, MCP server, and search index
**Researched:** 2026-02-26
**Confidence:** HIGH (Roslyn symbol issues verified via GitHub issues + official API docs), MEDIUM (MCP pitfalls verified via official .NET blog + nearform implementation guide), MEDIUM (search/tokenization patterns from ecosystem tools)

---

## Critical Pitfalls

### Pitfall 1: Symbol Identity Instability Across Compilations

**What goes wrong:**
The `SymbolId` derived from Roslyn's `ISymbol.GetDocumentationCommentId()` or `ToDisplayString()` is not guaranteed stable across renames, generic parameter changes, or partial-class splits. Refactoring a type name silently invalidates every persisted `SymbolId`, breaking snapshot diffs, index lookups, and cross-snapshot references without any compile error.

**Why it happens:**
Developers assume the XML documentation ID or display string is a durable key because it is compiler-generated. In reality, it encodes the current name, arity, and namespace — all of which change on rename. The problem goes undetected until a diff between two snapshots produces spurious "added/removed" entries instead of "renamed."

**How to avoid:**
- Define the `SymbolId` spec explicitly in Tier 0.1 before any other work; do not derive it ad-hoc from `ISymbol`.
- Store a `PreviousIds` list on `SymbolNode` to support rename tracking.
- Version the `SymbolId` schema explicitly (v1, v2) and write a migration path before adding polyglot support.
- Use `SymbolEqualityComparer` (not reference or string equality) when comparing live `ISymbol` instances during the same compilation session.
- Add a golden-file test: snapshot a fixture project, rename a type, re-snapshot, and assert the diff engine produces a "renamed" finding rather than "deleted + added."

**Warning signs:**
- Diff results show large numbers of "added" and "removed" symbols after a routine rename refactor.
- Unit tests compare `ISymbol` instances with `==` or `string.Equals` on `ToDisplayString()` output.
- No `SymbolId` spec document exists before implementation begins.

**Phase to address:** Tier 0.1 (Stable Symbol Identity) — must be locked before snapshot pipeline work begins.

---

### Pitfall 2: STDIO Contamination Breaking the MCP Protocol Stream

**What goes wrong:**
Any `Console.WriteLine`, `Debug.WriteLine`, unhandled exception stack trace, or .NET startup banner written to `stdout` corrupts the MCP message framing. The client receives malformed JSON-RPC and either fails silently or disconnects. This is especially insidious because the server appears to start correctly in isolation.

**Why it happens:**
.NET logging defaults (`ILogger` with a console provider) write to `stdout`. ASP.NET / Aspire host startup messages also write to `stdout`. Developers add debug `Console.Write` calls during development and forget to remove them. Third-party libraries may also write to `stdout`.

**How to avoid:**
- Configure all `ILogger` sinks to write exclusively to `stderr` or a file sink from day one.
- Disable the default console logging provider in the MCP host process.
- Add an integration test that starts the MCP server as a child process via stdio, sends a `ping`, and asserts the response is valid JSON-RPC with zero stray bytes before the first message.
- Treat any `Console.Write*` call in the server project as a build warning using a Roslyn analyzer or code style rule.

**Warning signs:**
- MCP client reports "unexpected token" or parse errors on first connection.
- Server works in unit tests but fails when Claude Desktop or `mcp inspect` connects via stdio.
- Log output appears intermixed with tool response JSON in captured stdio streams.

**Phase to address:** Tier 0.3 (Narrow MCP tool surface) — enforce at server scaffold time before any tools are added.

---

### Pitfall 3: MSBuildWorkspace Memory Growth and Compilation Object Leakage

**What goes wrong:**
`MSBuildWorkspace.OpenSolutionAsync()` loads every project and its transitive dependencies into memory. Each `CSharpCompilation` object holds loaded assemblies that cannot be garbage-collected without an `AssemblyLoadContext` boundary. Repeated snapshot builds in a long-running process grow memory unboundedly, eventually causing OOM or severe GC pressure.

**Why it happens:**
Roslyn's workspace API is designed for IDE scenarios where memory is managed by the IDE host. Standalone tools that call `OpenSolutionAsync` in a loop, or that build a workspace per snapshot request, silently accumulate `Compilation` objects and their associated metadata references.

**How to avoid:**
- Isolate each snapshot build in a dedicated `AssemblyLoadContext` (collectible) and dispose it after artifact writing.
- Alternatively, run Roslyn ingestion as a short-lived worker process invoked per build; the process exit reclaims all memory.
- Do not hold `Compilation` or `SemanticModel` objects in long-lived caches; extract needed data (symbols, spans) into plain data structures (the `SymbolNode` graph) immediately and release the compilation.
- Add a memory regression test: build the same fixture project 10 times in a loop and assert no unbounded growth in Gen2 GC collections.

**Warning signs:**
- Memory use grows proportionally to the number of snapshot builds in a single run.
- `CSharpCompilation` objects appear in memory profiler snapshots long after snapshot writing completes.
- Process memory is not released between builds when running in the Aspire host.

**Phase to address:** Tier 0.2 (Snapshot pipeline) — design the compilation lifecycle boundary before the pipeline is built.

---

### Pitfall 4: Non-Deterministic SymbolGraphSnapshot Serialization

**What goes wrong:**
Two runs on the same source tree produce `SymbolGraphSnapshot` JSON with different field ordering, dictionary key ordering, or floating-point representations. Golden-file tests randomly fail, snapshot hashing is unreliable, and diff engines report phantom changes between identical snapshots.

**Why it happens:**
`System.Text.Json` serializes `Dictionary<K,V>` in insertion order, which varies across runs if populated from Roslyn's unordered symbol enumeration. `HashSet<T>` has no stable enumeration order. LINQ operators like `GroupBy` do not guarantee ordering. Custom hash functions may differ between .NET versions.

**How to avoid:**
- Sort all collections before serialization: symbols by `SymbolId.ToString()`, edges by `(Source, Target, Kind)`, members by kind then name.
- Write a determinism test as the very first snapshot test: serialize the same fixture twice in the same process and assert byte-for-byte identical output.
- Use `JsonSerializerOptions` with explicit property naming policy and disable any options that introduce variability.
- Store a `ContentHash` on the snapshot computed from the sorted canonical form, and assert the hash is stable across runs.

**Warning signs:**
- Golden-file tests fail intermittently ("flaky") rather than consistently.
- Diff results show changes between two snapshots of the same unchanged codebase.
- Different developers' CI runs produce different snapshot file hashes.

**Phase to address:** Tier 0.1 (Stable Symbol Identity) and Tier 0.2 (Snapshot pipeline) — enforce determinism in the first serialization test.

---

### Pitfall 5: XML Documentation Binding Failures on Generic and Partial Types

**What goes wrong:**
The XML documentation parser fails to bind `<member name="...">` entries to live `ISymbol` instances for generic types with arity > 1, partial classes spread across files, and overloaded methods with `ref`/`out` parameters. Affected symbols appear in the symbol graph with no documentation, silently reducing coverage with no error.

**Why it happens:**
The XML doc ID format (e.g., `M:Namespace.Type.Method``1(System.String,``0)`) uses backtick-encoded arities and positional generic parameter references that are non-obvious to parse. The Roslyn API does not provide a built-in bidirectional mapping between XML doc IDs and `ISymbol` references on external assemblies, and the mapping has known edge cases with generic constraints, `inheritdoc`, and `typeparamref`.

**How to avoid:**
- Use `DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation)` (Roslyn internal utility) for round-trip lookup; do not hand-roll the XML ID → ISymbol mapping.
- Build unit tests specifically for: generic types with multiple type parameters, partial classes, overloaded methods with `ref`/`out`, indexers, explicit interface implementations, and operators.
- Track "unbound doc entries" as a first-class metric in the ingestion pipeline and fail the build if binding rate drops below a configurable threshold.

**Warning signs:**
- Generic types appear in the symbol graph with no `DocComment` attached.
- The binding success rate reported by the ingestion pipeline is below 95% on a well-documented codebase.
- Unit tests only cover simple non-generic methods.

**Phase to address:** Tier 0.2 (Snapshot pipeline) — implement and test binding edge cases before indexing is built on top.

---

### Pitfall 6: MCP Tool Surface Expanding Beyond Read-Only Scope

**What goes wrong:**
Incremental feature pressure ("it would be useful if the tool could also write...") causes the MCP tool surface to grow from read-only to read-write, exposing file creation, project modification, or build execution via tools that were originally purely informational. A compromised or misbehaving agent can trigger destructive operations.

**Why it happens:**
The project plan explicitly defers write-capable tools to later tiers, but ad-hoc convenience additions during development bypass this gate. MCP tools that call into Roslyn workspace APIs have natural access to compilation artifacts and file paths, making it easy to accidentally expose write paths.

**How to avoid:**
- Define a static allowlist of MCP tool names at project inception; any addition requires explicit sign-off in code review.
- All file path parameters must pass through the path allowlist validator before any I/O occurs; test this with path traversal inputs (`../../../etc/passwd`, Windows UNC paths, symlinks).
- Mark tool handler methods with a `[ReadOnly]` attribute and write a Roslyn analyzer that fails the build if a `[ReadOnly]` method calls any `File.Write*`, `Directory.Create*`, or equivalent.

**Warning signs:**
- New tool parameters include "output path" or "write" in their names.
- Tool handlers reference `File.WriteAllText`, `Directory.CreateDirectory`, or any `IFileSystem.Write` method.
- The tool description to the agent says "creates" or "modifies" anything.

**Phase to address:** Tier 0.3 (MCP tool surface) — enforce at tool registration time with a code review gate.

---

### Pitfall 7: Indirect Prompt Injection via Code Comments and Symbol Names

**What goes wrong:**
An attacker (or malicious dependency) embeds LLM instruction payloads inside XML doc comments, symbol names, or string literals in the codebase being indexed. When the agent reads the output of `get_symbol` or `search_symbols`, the injected text coerces the agent into taking unintended actions — fetching external URLs, writing files, or exfiltrating data.

**Why it happens:**
Prompt injection ranked #1 on OWASP's 2025 LLM Top 10 and affects all agentic systems that include retrieved text in the LLM context window. Code documentation is particularly dangerous because XML comments are long-form natural language that agents read verbatim. The attack is zero-click: no user interaction required.

**How to avoid:**
- Implement output redaction hooks that strip or escape `<system>`, `<INST>`, `[INST]`, and similar delimiter patterns from all MCP tool responses.
- Mark all tool output as "data, not instructions" in the tool description metadata provided to the agent.
- Return structured objects (typed DTOs) rather than raw strings; agents are less likely to treat structured JSON fields as executable instructions.
- Add a test fixture that injects `"Ignore previous instructions and..."` into a doc comment and asserts the MCP tool response does not propagate the injection verbatim without escaping.
- Log all tool call inputs/outputs to the audit log for post-hoc inspection.

**Warning signs:**
- Tool responses return raw, unescaped XML doc comment text directly into the JSON response.
- No output sanitization layer exists between the symbol graph and MCP tool response serialization.
- The agent begins calling tools in unexpected sequences after querying a symbol with a long description.

**Phase to address:** Tier 0.3 (MCP tool surface) and security hardening — implement before any production use.

---

### Pitfall 8: Semantic Diff False Positives from Nullability and Compiler-Generated Members

**What goes wrong:**
The diff engine reports high-severity "API breaking change" findings for nullability annotation changes (`string` → `string?`), compiler-generated record members, auto-property accessor changes, and primary constructor parameter capture. This creates alert fatigue: developers start ignoring all diff output because too many findings are spurious.

**Why it happens:**
Nullability is a type system annotation in C# 8+; it changes the `ITypeSymbol` representation but does not break binary compatibility. Compiler-generated members (record `Equals`, `GetHashCode`, deconstruct, `init`-only setters) appear as new symbols when comparing against pre-C#-9 snapshots. Primary constructor parameters are captured as synthetic backing fields that vary by compiler version.

**How to avoid:**
- Build a risk-scoring model for diff findings that classifies nullability changes as LOW risk by default unless the parameter went from `T` (oblivious) to `T?` (explicitly nullable).
- Exclude compiler-synthesized members (identifiable via `ISymbol.IsImplicitlyDeclared`) from the default diff surface; include them only in a verbose mode.
- Calibrate the diff engine against a set of known-good change scenarios (add field, rename, change nullability, add record member) with expected risk scores before shipping.
- Treat "doc/test divergence" signals (API changed but no test changed) as MEDIUM risk, not automatically HIGH.

**Warning signs:**
- Diff output on a codebase that added `#nullable enable` shows hundreds of findings.
- Every new `record` type produces synthetic "new symbol" findings.
- Developers report ignoring diff output because of too many false positives.

**Phase to address:** Tier 1.1 (Semantic diff engine) — define risk classification rules before implementing the diff output format.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Use `ISymbol.ToDisplayString()` as the persisted `SymbolId` | Works immediately for simple cases | Breaks on rename; no migration path; unstable across compiler versions | Never — define a proper `SymbolId` spec first |
| Store raw XML doc string without binding to symbols | Fast to implement `XmlDocParser` | No relationship between doc and symbol; coverage gaps invisible | MVP only if binding is a tracked backlog item with a phase deadline |
| Skip path allowlist validation in early tool handlers | Faster tool development | Path traversal vulnerability from day one | Never — implement allowlist before first tool ships |
| In-memory `InMemorySearchIndex` with contains-match | Passes initial tests | Returns irrelevant results; ranking unusable by agents | Only as a placeholder stub until BM25 is implemented in same phase |
| Single `MSBuildWorkspace` instance reused across builds | Simpler code | Memory leak; OOM in long-running server | Never for server processes; acceptable for short-lived CLI tools only |
| Serialize `HashSet<SymbolNode>` without sorting | Trivially simple | Non-deterministic snapshots; broken golden-file tests | Never — sort before serialize always |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Roslyn `MSBuildWorkspace` | Opening workspace without registering MSBuild defaults causes silent empty solution loads | Call `MSBuildLocator.RegisterDefaults()` before `MSBuildWorkspace.Create()` |
| Roslyn `SemanticModel` | Requesting semantic model per-file in a loop causes redundant full-solution compilation | Request the `Compilation` once and call `compilation.GetSemanticModel(tree)` per tree |
| MCP C# SDK (preview) | Registering tools with overlapping parameter names confuses model routing | Use distinct, descriptive parameter names; validate schema with `mcp inspect` before first use |
| `System.Text.Json` + `ISymbol` | Attempting to serialize `ISymbol` directly hits circular references and internal Roslyn types | Project all data into plain `SymbolNode` / `SymbolEdge` records before any serialization |
| .NET Aspire host + MCP stdio | Aspire's default `ILogger` console sink writes to `stdout`, corrupting MCP stdio stream | Redirect all logging to `stderr` in the MCP server process configuration |
| XML doc `inheritdoc` | `<inheritdoc/>` tags are not expanded by the compiler XML doc output; the raw tag appears | Expand `inheritdoc` during ingestion using the symbol hierarchy before storing `DocComment` |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Loading full `MSBuildWorkspace` on every MCP tool call | Tool response latency > 30s; process memory spikes on each call | Build snapshot once per codebase version; serve queries from the persisted artifact | At codebase sizes > 10 projects |
| BM25 index rebuilt from scratch on each search | Search latency grows linearly with codebase size | Build index at snapshot time; persist index artifact alongside snapshot | At > 5,000 symbols |
| Returning full `SymbolNode` JSON tree in `search_symbols` response | Token budget exhausted; agent context window fills with symbol data | Return slim search hits (id, name, kind, score) and let agent call `get_symbol` for full details | At > 20 search results per query |
| Walking all `INamespaceSymbol` members recursively without caching | Full symbol walk takes 10-30s on large solutions | Walk once during snapshot build; do not re-walk on query | At > 50,000 symbols (enterprise solution) |
| Keeping `SemanticModel` alive across async boundaries | GC cannot collect; memory grows with concurrent tool calls | Obtain, extract data, dispose in a single synchronous block per file | Any concurrent MCP tool calls |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Accepting file path parameters from MCP tool calls without validation | Path traversal — agent can read arbitrary files outside the allowed project root | Normalize all paths with `Path.GetFullPath()` and assert they are under the configured allowlist root |
| Logging full MCP request payloads without redaction | Secrets in source code (connection strings, API keys in constants) written to audit log | Apply secret-pattern redaction (regex for connection strings, bearer tokens) before audit log writes |
| Returning absolute file system paths in symbol source spans | Leaks developer machine layout / CI environment details to agent context | Strip path prefix to project-relative form before including `SourceSpan` in MCP responses |
| Using a single shared `SymbolGraphSnapshot` instance across concurrent requests | Race condition; partial writes visible to readers | Snapshots are immutable once written; read from file only; never mutate after persist |
| Trust-forwarding the MCP client's identity token to downstream services | Confused deputy — agent impersonates a privileged user to backend services | MCP server must authenticate itself independently; never propagate client tokens |

---

## "Looks Done But Isn't" Checklist

- [ ] **XML doc binding:** Parser returns `DocComment` objects but binding rate to symbols never measured — verify coverage metric is implemented and checked per build
- [ ] **Search index:** `InMemorySearchIndex` stub replaced with BM25 — verify the replacement is actually called, not the stub fallback
- [ ] **Path allowlist:** Allowlist validator exists but not wired into all tool handlers — verify every tool handler with a path parameter calls the validator before any I/O
- [ ] **Determinism:** Snapshot serialization passes individual tests but never tested for bit-exact cross-run stability — run the same build twice and diff the output files
- [ ] **Stdio safety:** MCP server tools pass unit tests but stdout contamination not tested end-to-end — start the server as a subprocess and capture raw stdout bytes
- [ ] **Audit logging:** Logging middleware is wired in but tool call audit entries never inspected — assert audit log contains expected entries after a tool call integration test
- [ ] **Diff engine risk scores:** Diff engine produces findings but no calibration against known-good and known-bad change scenarios — add a parameterized test covering the standard change taxonomy
- [ ] **Memory lifecycle:** Snapshot builds complete but `Compilation` objects are not verified to be released — run snapshot build 5 times in a loop and assert Gen2 GC count does not climb

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Symbol ID instability discovered after snapshot storage in production | HIGH | Define stable ID spec; write migration script; rebuild all snapshots; update all foreign references |
| STDIO contamination discovered after Claude Desktop integration | LOW | Identify write site; redirect to stderr; redeploy server process |
| MSBuildWorkspace memory leak discovered in long-running server | MEDIUM | Introduce `AssemblyLoadContext` isolation or switch to per-request worker process model |
| Non-deterministic snapshots discovered after golden files diverge | MEDIUM | Audit all collection serialization; add sorting; rebuild all golden files |
| XML doc binding failures discovered on large codebase | MEDIUM | Audit unbound symbol count; add targeted edge-case tests; fix parser for failing ID patterns |
| Prompt injection via doc comments discovered post-deployment | MEDIUM | Add output redaction hook in tool response serialization; audit historical logs for injection patterns |
| Diff false positives discovered causing alert fatigue | MEDIUM | Implement risk classification model; recalibrate scores; add suppression rules for known-safe patterns |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Symbol identity instability | Tier 0.1 (Stable Symbol Identity) | Golden-file test: rename fixture symbol, assert diff shows "renamed" not "deleted+added" |
| STDIO contamination | Tier 0.3 (MCP tool surface, initial scaffold) | Integration test: capture raw stdout bytes from server subprocess, assert valid JSON-RPC only |
| MSBuildWorkspace memory leak | Tier 0.2 (Snapshot pipeline) | Loop test: build same project 10 times, assert Gen2 GC count bounded |
| Non-deterministic serialization | Tier 0.1 + Tier 0.2 | Determinism test: serialize same fixture twice, assert byte-identical output |
| XML doc binding failures | Tier 0.2 (Snapshot pipeline) | Parameterized unit tests: generics, partials, overloads, operators, explicit impls |
| MCP tool surface scope creep | Tier 0.3 + all subsequent tiers | Static analysis: Roslyn analyzer or code review gate rejecting write-capable tool handlers |
| Prompt injection via doc comments | Tier 0.3 (security hardening) | Test fixture: inject instruction payload in doc comment, assert response escapes/strips it |
| Diff false positives | Tier 1.1 (Semantic diff engine) | Calibration test suite: known-good changes produce expected risk scores |

---

## Sources

- [Symbols and types in Roslyn — Route to Roslyn](https://route2roslyn.netlify.app/symbols-for-dummies/) — symbol equality, SymbolEqualityComparer guidance
- [ISymbol Interface — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.isymbol?view=roslyn-dotnet-4.9.0) — official API reference
- [Memory leak on Microsoft.CodeAnalysis.Scripting — dotnet/roslyn #41348](https://github.com/dotnet/roslyn/issues/41348) — compilation object memory retention
- [MSBuildWorkspace with ProjectReference fails compilation — dotnet/roslyn #36072](https://github.com/dotnet/roslyn/issues/36072) — workspace loading pitfalls
- [Using MSBuildWorkspace — DustinCampbell gist](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3) — MSBuildLocator prerequisite
- [Implementing MCP: Tips, Tricks and Pitfalls — Nearform](https://nearform.com/digital-community/implementing-model-context-protocol-mcp-tips-tricks-and-pitfalls/) — STDIO contamination, tool design, security
- [Build an MCP server in C# — .NET Blog](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/) — official C# SDK guidance
- [New Prompt Injection Attack Vectors Through MCP Sampling — Palo Alto Unit 42](https://unit42.paloaltonetworks.com/model-context-protocol-attack-vectors/) — MCP-specific prompt injection threat model
- [LLM01:2025 Prompt Injection — OWASP Gen AI Security Project](https://genai.owasp.org/llmrisk/llm01-prompt-injection/) — prompt injection classification
- [Retrieving Symbol from DocumentationCommentId — dotnet/roslyn #16786](https://github.com/dotnet/roslyn/issues/16786) — XML doc ID ↔ ISymbol round-trip
- [XML doc generics + inheritdoc + typeparamref bug — dotnet/roslyn #54069](https://github.com/dotnet/roslyn/issues/54069) — XML doc edge cases
- [APIDiff: Detecting API breaking changes — Semantic Scholar](https://www.semanticscholar.org/paper/APIDiff:-Detecting-API-breaking-changes-Brito-Xavier/a02f93289afe58989589abafc6ae098ef8e544a8) — semantic diff false positive research
- [Improving C# Source Generation Performance with Custom Roslyn Workspace — Jay Lee](https://jaylee.org/archive/2019/03/04/custom-roslyn-msbuild-workspace.html) — MSBuildWorkspace performance

---
*Pitfalls research for: .NET code documentation agent framework (Roslyn + MCP + search index)*
*Researched: 2026-02-26*

---
---

# Pitfalls Research — Addendum: v1.2 Multi-Project and Solution-Level Graphs

**Domain:** Adding multi-project symbol graphs, cross-project dependency tracing, .sln ingestion, and NuGet stub resolution to an existing single-project Roslyn-based documentation framework
**Researched:** 2026-03-01
**Confidence:** HIGH (Roslyn cross-project issues confirmed via official GitHub discussions and Roslyn source), HIGH (MSBuildWorkspace load failures confirmed via multiple open issues + community guides), MEDIUM (incremental invalidation patterns from F# compiler service issue analysis)

---

## Critical Pitfalls (v1.2-Specific)

### Pitfall M1: Symbol Identity Breaks Across Project Compilations

**What goes wrong:**
The same type (e.g., a shared library's `IFoo`) appears in multiple `CSharpCompilation` instances — one per loaded project. Roslyn does NOT guarantee `SymbolEqualityComparer.Default` equality across compilations. When building cross-project edges, `SymbolNode` for `MyLib.Foo` resolved from Project A's compilation and the same symbol from Project B's compilation appear as two distinct, non-equal symbols. The unified graph accumulates phantom duplicates and missed cross-project connections.

**Why it happens:**
Each `CSharpCompilation` builds its own `INamedTypeSymbol` objects. Even when two projects reference the same type from the same assembly, Roslyn creates separate instances per compilation context. Developers assume `==` or `.Equals()` works across compilations because the fully-qualified name is identical.

**How to avoid:**
Use `SymbolId` (documentation comment ID string, e.g., `T:MyLib.Foo`) as the canonical cross-project deduplication key — never use Roslyn `ISymbol` object references across compilation boundaries. Key all `SymbolNode` entries and all edge lookups on `SymbolId` strings. When merging two project snapshots into the unified solution graph, merge on `SymbolId`, not on `ISymbol`.

**Warning signs:**
- Duplicate `SymbolNode` entries with identical `SymbolId` values but different object origins in the merged graph.
- `get_references` returning doubled results because a symbol was indexed from two compilation contexts.
- Cross-project edges pointing from a `SymbolId` in Project A to a different object representing the same type in Project B.

**Phase to address:** Phase implementing `SolutionSymbolGraphBuilder` and unified graph merge — enforce `SymbolId`-keyed deduplication before any cross-project edge is added.

---

### Pitfall M2: MSBuildWorkspace Silent Load Failures

**What goes wrong:**
`MSBuildWorkspace.OpenSolutionAsync()` silently returns an incomplete `Solution` — projects may have zero documents, zero references, or missing metadata — without throwing exceptions. The code appears to succeed and the graph is built, but it covers only a fraction of the codebase. MCP tools return confidently wrong results.

**Why it happens:**
`MSBuildWorkspace` fails gracefully: unrecognized SDKs, missing SDK paths, platform mismatches (AnyCPU vs x64 set differently in `.sln` vs `.csproj`), or mismatched MSBuild assembly versions all cause partial loads. Without a `WorkspaceFailed` event handler, these are swallowed silently. Additionally, `MSBuildLocator.RegisterDefaults()` must be called before any `Microsoft.Build.*` type touches the AppDomain — calling it after DI initialization causes silent failures.

**How to avoid:**
1. Subscribe to `workspace.WorkspaceFailed` before opening and collect all `WorkspaceDiagnosticEventArgs`. Treat any `Kind == WorkspaceDiagnosticKind.Failure` as a hard error.
2. Call `MSBuildLocator.RegisterDefaults()` as the very first statement in `Program.cs`, before all DI container setup.
3. After loading, validate that every project listed in the `.sln` has at least one `Document`. Fail loudly with collected diagnostics if any project is empty.
4. Expose a sanitized diagnostic count in the `ingest_solution` MCP tool response so callers know which projects failed.

**Warning signs:**
- `workspace.CurrentSolution.Projects` count is less than the project count listed in the `.sln`.
- Any project has `Documents.Count == 0`.
- `WorkspaceFailed` event fires with message containing "cannot be opened", "unrecognized", or "failed to load".
- `UnresolvedMetadataReference` objects appear in `project.MetadataReferences`.

**Phase to address:** Phase implementing `.sln` ingestion / `SolutionIngestionService` — validate load completeness before snapshot construction begins.

---

### Pitfall M3: Multi-Targeting Projects Create Duplicate Snapshots

**What goes wrong:**
A project targeting `net10.0;net48` produces two logical compilations in `MSBuildWorkspace`. Without handling, the ingestion pipeline creates two `SymbolGraphSnapshot`s for the same project (or ingests the same symbols twice into the unified graph), doubling all symbol counts and creating phantom cross-project edges.

**Why it happens:**
`MSBuildWorkspace` represents multi-targeted projects as multiple `Project` objects in the `Solution`, each with a different `Name` suffix (e.g., `MyLib (net10.0)` and `MyLib (net48)`). The existing single-project ingestion path does not expect this and has no deduplication strategy.

**How to avoid:**
Group `Solution.Projects` by `.FilePath` (the `.csproj` path) before ingesting. For multi-targeted projects, select only the highest/preferred TFM (the first, or a configured preference). This is sufficient for v1.2. Document the limitation explicitly in the `explain_solution` tool output.

**Warning signs:**
- `solution.Projects.Count` is significantly larger than the number of `.csproj` files in the solution.
- Multiple `Project` objects share the same `.FilePath` but have different `Name` values.
- Symbol count in the merged graph is exactly 2x or 3x what single-project ingestion produces.

**Phase to address:** Phase implementing solution project enumeration — add TFM deduplication before invoking per-project ingestion.

---

### Pitfall M4: Cross-Project Incremental Invalidation Is Too Narrow

**What goes wrong:**
The existing SHA-256 incremental ingestion re-parses only modified files within a project. When extended to multi-project graphs, a change in Project A's public interface should also invalidate Project B's cross-project edges that reference it. If the incremental logic only re-ingests A without re-evaluating B's cross-project references, the unified graph becomes stale: B's call graph still references the old signature, and `find_breaking_changes` misses cross-boundary breaks.

**Why it happens:**
Single-project incremental logic has no concept of dependency propagation. Re-ingesting a changed project only updates that project's snapshot — it does not trigger re-evaluation of downstream dependent projects.

**How to avoid:**
Build a project dependency graph at solution load time from `project.ProjectReferences`. When a project's snapshot is re-ingested, mark all downstream dependent projects as dirty and re-evaluate their cross-project edges (re-linking edges from fresh snapshots, not necessarily full re-ingestion). Use a topological sort of the project dependency graph to determine re-evaluation order.

**Warning signs:**
- After changing a public API in Project A, `get_references` from Project B still returns the old call site count.
- `find_breaking_changes` across projects reports no changes even though a public method signature changed.
- `GraphDiff` for a solution re-ingest shows changes only in the modified project, never in referencing projects.

**Phase to address:** Phase implementing cross-project edge resolution and incremental update logic.

---

### Pitfall M5: NuGet Metadata Stubs Pollute Full-Symbol Search Results

**What goes wrong:**
NuGet stub nodes (created for external package types appearing in cross-project reference resolution) are indexed into the BM25 search index alongside full `SymbolNode`s. Agents searching for `ILogger` now get both the full local symbol (if one exists) and dozens of NuGet stub entries from every project that references `Microsoft.Extensions.Logging`, cluttering results with low-value metadata-only hits.

**Why it happens:**
The existing `ISearchIndex.IndexAsync` takes a full `SymbolGraphSnapshot` and indexes everything in it. Stub nodes are structurally identical to full nodes (by design, for graph consistency) but have no `DocComment`, no `SourceSpan`, and no documentation value.

**How to avoid:**
Tag `SymbolNode`s created from NuGet metadata with a `NodeKind` discriminator (e.g., `NodeKind.NuGetStub` vs `NodeKind.Source`). In `BM25SearchIndex.IndexAsync`, skip all `NuGetStub` nodes. In MCP tool responses, include an `is_stub: true` flag if a stub node surfaces as a reference target. This keeps the search index over source symbols only.

**Warning signs:**
- `search_symbols "ILogger"` returns 15+ results when only 2-3 are from local source code.
- `get_symbol` on a stub node returns an empty `DocComment` and null `SourceSpan`.
- Stub node `SymbolId`s appear in the BM25 index document count.

**Phase to address:** Phase implementing NuGet stub node creation and unified graph — `NodeKind` must be defined before stub nodes are created.

---

### Pitfall M6: PathAllowlist Not Extended to Solution-Level Paths

**What goes wrong:**
The existing `PathAllowlist` enforcement (applied to all ChangeTools and DocTools) validates paths against a configured allowlist. Solution-level ingestion introduces `.sln` file paths, referenced project paths outside the primary source tree, and NuGet cache paths — none of which are in any existing allowlist. The server either silently rejects ingest requests or, if path enforcement is accidentally bypassed for new code paths, opens a read-access vector to arbitrary filesystem locations.

**Why it happens:**
The existing allowlist was designed for single-project source trees. Developers adding solution ingestion focus on functionality and forget to extend security boundaries. This is the same pattern that made the PathAllowlist retrofit necessary for ChangeTools in v1.1.

**How to avoid:**
Before implementing `SolutionIngestionService`, extend `DocAgentServerOptions` with explicit allowlist entries for: (a) the solution file path, (b) all project directories reachable from the solution, (c) an optional NuGet cache path (read-only, stub extraction only). Apply `PathAllowlist.IsAllowed()` at the top of every new solution-level ingestion method. Add unit tests that verify denial of paths outside the solution boundary — consistent with the existing pattern for DocTools and ChangeTools.

**Warning signs:**
- New `ingest_solution` method lacks a `PathAllowlist.IsAllowed()` guard at entry.
- `DocAgentServerOptions` has no field for solution-level path allowances.
- NuGet cache reads happen without path validation.
- Tests for the new ingestion path do not include a denial case.

**Phase to address:** Phase implementing `ingest_solution` MCP tool — security gate must be the first implementation step, not retrofitted.

---

### Pitfall M7: SnapshotRef Version Scheme Breaks at Solution Scale

**What goes wrong:**
The existing `SymbolGraphSnapshot` uses a single version field (monotonic counter or timestamp). At solution scale, incremental re-ingestion of individual projects means the solution graph is a composite of per-project snapshots with different version numbers. `diff_snapshots` comparing two `SnapshotRef`s by version number now has ambiguous semantics: "version 5" might mean the 5th ingestion of the whole solution, or the 5th ingestion of Project A only.

**Why it happens:**
Version schemes designed for single-project graphs assume a single linearizable history. Multi-project graphs have a per-project version vector, and any code assuming a single scalar version produces incorrect diffs or incorrect "latest snapshot" lookups.

**How to avoid:**
Introduce a `SolutionSnapshot` wrapper that holds: a solution-level monotonic version (bumped on any project re-ingest), a map of `{projectFilePath → SnapshotRef}`, and a combined graph hash. The existing `SymbolGraphSnapshot` per project remains unchanged. `diff_snapshots` at the solution level compares `SolutionSnapshot` pairs, delegating per-project diffs where project versions changed. Existing single-project tools continue working against individual `SymbolGraphSnapshot`s unmodified.

**Warning signs:**
- `diff_snapshots` returns an empty diff after a partial solution re-ingest (solution-level version not bumped).
- `explain_solution` shows stale project data after one project was re-ingested.
- `SnapshotStore` "latest" lookup returns a single-project snapshot instead of the solution composite.

**Phase to address:** Phase designing the unified graph data model / `SolutionSnapshot` type — must be designed before any storage or tool changes.

---

## Technical Debt Patterns (v1.2 Additions)

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Load only the first TFM for multi-targeted projects | Avoids TFM merge complexity in v1.2 | Misses conditional compilation differences; misleads agents about API shape | Acceptable for v1.2; document the limitation explicitly in `explain_solution` output |
| Eagerly load all NuGet metadata on solution open | Simpler than lazy resolution | Dramatically increases load time on large solutions | Never — use lazy/on-demand stub creation keyed on first reference encounter |
| Skip `WorkspaceFailed` diagnostics in first iteration | Faster to implement | Silent partial loads; agents reason over incomplete graphs | Never — hook `WorkspaceFailed` before any other work |
| Index all nodes including NuGet stubs | Simpler indexing pipeline | Search quality degrades; agents confused by stub results | Never — filter at index time |
| Reuse single scalar snapshot version for solution | Zero migration cost | Ambiguous diffs after partial re-ingest | Never — introduce `SolutionSnapshot` wrapper from the start |

---

## Integration Gotchas (v1.2 Additions)

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| `MSBuildLocator` | Calling `RegisterDefaults()` after DI setup has referenced a `Microsoft.Build.*` type | Call `MSBuildLocator.RegisterDefaults()` as the very first line of `Program.cs`, before all other initialization |
| `MSBuildWorkspace` project references | Assuming `project.ProjectReferences` gives the full transitive graph | `ProjectReferences` is direct references only; build transitive closure with topological sort over `Solution.Projects` |
| Platform/configuration mismatch in `.sln` | Default platform in `.sln` differs from project default causing `UnresolvedMetadataReference` | Pass `globalProperties: new Dictionary<string,string>{{"Configuration","Release"},{"Platform","AnyCPU"}}` to `MSBuildWorkspace.Create()` |
| NuGet package metadata resolution | Trying to load NuGet packages through workspace compilation references to get type stubs | Extract metadata-only assembly paths from `project.MetadataReferences` after workspace load; use `MetadataReference.CreateFromFile()` for stub extraction only |
| Path casing in cross-platform environments | Two `ProjectReference` entries with different casing treated as distinct projects by workspace | Normalize all project paths with `Path.GetFullPath()` before deduplication; lowercase on case-insensitive filesystems |
| Existing `PathAllowlist` in `DocAgentServerOptions` | Adding solution paths without updating the allowlist | Extend `DocAgentServerOptions` with `SolutionPath` and `AdditionalProjectRoots` properties validated through the same `PathAllowlist` infrastructure |

---

## Performance Traps (v1.2 Additions)

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Loading entire solution with `MSBuildWorkspace` on every `ingest_solution` call | Ingest takes 30-120 seconds; MCP tool times out | Load workspace once, cache the `Solution`; re-open only on explicit re-ingest trigger | Solutions with more than 10 projects |
| Eagerly resolving all NuGet package metadata references as full compilations | Memory spikes to multiple GB; OOM on large solutions | Resolve NuGet references as `MetadataReference` (assembly metadata only), never as full `Compilation` | Solutions with more than 20 NuGet packages |
| Building cross-project edges by iterating all symbol pairs (O(N²)) | Edge construction takes minutes on medium solutions | Build edges during ingestion by walking `InvocationExpression` → `SemanticModel.GetSymbolInfo()` → lookup in cross-project symbol table | Graphs with more than 5,000 symbols per project |
| Re-indexing the entire BM25 index on each partial solution re-ingest | BM25 index rebuild takes longer than the ingestion itself | Use per-project index segments; merge on query (Lucene.Net supports multi-reader search over per-segment indexes) | Solutions re-ingested more than once per minute |

---

## Security Mistakes (v1.2 Additions)

| Mistake | Risk | Prevention |
|---------|------|------------|
| Allowing `ingest_solution` to accept arbitrary `.sln` paths without allowlist check | Agent or prompt injection could trigger ingestion of sensitive codebases outside the intended project | Apply `PathAllowlist.IsAllowed()` to `.sln` path as the first operation in `ingest_solution` handler; return opaque denial consistent with existing tools |
| Exposing NuGet cache paths in MCP tool responses | Leaks filesystem layout of the host machine; NuGet cache path is PII-adjacent on developer machines | Filter `SourceSpan.FilePath` for stub nodes; never return NuGet cache paths in tool output |
| Logging full workspace diagnostics (from `WorkspaceFailed`) to MCP response | Diagnostic messages contain absolute paths, internal SDK versions, and MSBuild property values | Log diagnostics internally via `ILogger`/OpenTelemetry; return only a sanitized error code and count to the MCP caller |
| Cross-project reference traversal without cycle detection | A corrupt `.sln` with circular `ProjectReference` entries causes infinite graph traversal | Add visited-set cycle detection and a max-depth guard in the project dependency graph builder |

---

## "Looks Done But Isn't" Checklist (v1.2 Additions)

- [ ] **Solution loading:** Validates that every project in the `.sln` has at least one `Document` loaded — not just that `OpenSolutionAsync` returned without throwing.
- [ ] **Cross-project edges:** Verifies edges are bidirectional where appropriate (caller has edge to callee AND callee has back-reference count updated) — not just that edges were added to the graph.
- [ ] **NuGet stub nodes:** Confirms stub nodes are excluded from the BM25 search index — not just that stub nodes exist in the graph.
- [ ] **PathAllowlist:** Confirms `ingest_solution` has a path denial unit test (not just a permission-allowed test) — consistent with the pattern established for DocTools and ChangeTools.
- [ ] **Multi-targeting:** Verifies that a project targeting two TFMs produces one `SymbolGraphSnapshot`, not two — check document count against `.csproj` file count.
- [ ] **Incremental invalidation:** After changing a public method in Project A, confirms Project B's cross-project edges referencing that method are re-evaluated — not just that Project A's snapshot version was bumped.
- [ ] **Determinism:** Confirms ingesting the same solution twice produces byte-identical snapshots — run the existing determinism test suite against solution-level snapshots.
- [ ] **`explain_solution` tool:** Returns a meaningful architecture summary even when NuGet stub nodes are present — does not include stub noise in the overview.

---

## Recovery Strategies (v1.2 Additions)

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Symbol identity collision in merged graph | MEDIUM | Re-key all `SymbolNode`s in the merged graph by `SymbolId` string; drop duplicates; rebuild cross-project edges; re-index |
| Silent partial load discovered after deployment | HIGH | Add `WorkspaceFailed` handler retroactively; expose `/diagnose_solution` tool; ask user to re-trigger ingest; graph is valid for loaded projects only |
| Multi-targeting duplicates already in graph | LOW | Run deduplication pass: group by `SymbolId`, keep the node with more documentation, delete others; rebuild index |
| NuGet stubs already indexed in BM25 | LOW | Drop and rebuild BM25 index with `NodeKind` filter in place; Lucene.Net supports full rebuild |
| PathAllowlist gap for solution paths | HIGH | Immediately disable `ingest_solution` tool; add missing allowlist checks; audit logs to determine what was accessed; re-enable after fix |
| Version scheme collision (solution vs project versions) | MEDIUM | Introduce `SolutionSnapshot` wrapper; migrate existing per-project `SnapshotRef`s as a map within it; update `SnapshotStore` lookup logic |

---

## Pitfall-to-Phase Mapping (v1.2)

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Symbol identity across compilations | Unified graph data model (define `SymbolId`-keyed merge) | Test: ingest two projects sharing a type, confirm single `SymbolNode` in merged graph |
| MSBuildWorkspace silent failures | Solution ingestion infrastructure (add `WorkspaceFailed` handler + load validation) | Test: provide a broken `.sln`; confirm error is surfaced, not swallowed |
| Multi-targeting duplicates | Solution project enumeration (group by `.FilePath` before ingestion) | Test: ingest a multi-targeted project, confirm one snapshot produced |
| Cross-project incremental invalidation | Incremental update logic (add dependency propagation) | Test: change public API in Project A, re-ingest, confirm Project B edges updated |
| NuGet stubs polluting search | NuGet stub node creation + indexing (add `NodeKind` filter at index time) | Test: confirm `search_symbols` returns zero stub nodes |
| PathAllowlist not extended | `ingest_solution` MCP tool (security gate first) | Test: path outside allowlist returns opaque denial |
| SnapshotRef version scheme ambiguity | `SolutionSnapshot` type design (before storage or tools) | Test: partial re-ingest bumps solution version; `diff_snapshots` reflects only changed projects |

---

## Sources (v1.2 Additions)

- [Roslyn: Duplicate symbols for the same types across a solution #69751](https://github.com/dotnet/roslyn/discussions/69751) — symbol identity across compilations
- [Roslyn: Public API to compare symbols across different compilations #62465](https://github.com/dotnet/roslyn/issues/62465) — cross-compilation symbol comparison
- [Roslyn: Symbol equality #3058](https://github.com/dotnet/roslyn/issues/3058) — SymbolEqualityComparer guidance
- [Roslyn: Duplicate projects in solution loaded into MSBuildWorkspace #16262](https://github.com/dotnet/roslyn/issues/16262) — path casing duplicate project pitfall
- [Roslyn: MSBuildWorkspace projects have 0 references #15479](https://github.com/dotnet/roslyn/issues/15479) — silent load failures
- [Roslyn: Huge performance drop in opening projects #76679](https://github.com/dotnet/roslyn/issues/76679) — workspace load performance
- [Roslyn: MSBuildWorkspace Unresolved Metadata Reference Exception](https://www.pumascan.com/resources/roslyn-unresolved-metadata-reference/) — platform mismatch causing metadata reference failures
- [Using MSBuildWorkspace — Dustin Campbell](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3) — MSBuildLocator initialization order requirement
- [Using the Roslyn APIs to Analyse a .NET Solution — Steve Gordon](https://www.stevejgordon.co.uk/using-the-roslyn-apis-to-analyse-a-dotnet-solution) — solution loading patterns
- [MSBuildLocator: Extend assembly loading warning #322](https://github.com/microsoft/MSBuildLocator/issues/322) — implicit dependency loading issues
- [Roslyn: Multi-targeting diagnostic nodes issue #6820](https://github.com/dotnet/project-system/issues/6820) — multi-targeting TFM ambiguity
- [F# incremental builders cache thrashing cross-project references #10217](https://github.com/dotnet/fsharp/issues/10217) — cross-project incremental invalidation patterns
- DocAgentFramework codebase: `docs/Architecture.md`, `.planning/PROJECT.md` (analyzed directly for integration-specific pitfalls)

---
*v1.2 addendum: Multi-project / solution-level symbol graph extension (DocAgentFramework v1.2)*
*Researched: 2026-03-01*
