# Architecture Research

**Domain:** .NET code documentation intelligence system with MCP server
**Researched:** 2026-02-26
**Confidence:** HIGH (Roslyn SDK — official docs; LSIF pipeline — official spec; system structure — grounded in existing codebase contracts)

## Standard Architecture

### System Overview

Code intelligence systems of this type follow a well-established layered pipeline. The Roslyn compiler platform and LSIF both use the same fundamental structure: source material flows through parse/ingest stages into a normalized graph, which is indexed for query, then served via a narrow API surface. This project's existing abstractions map cleanly onto this canonical pattern.

```
┌──────────────────────────────────────────────────────────────────┐
│                         INGESTION LAYER                          │
│                                                                  │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────────┐ │
│  │ IProjectSource│   │  IDocSource  │   │    IXmlDocParser     │ │
│  │ (discovery)  │   │  (loading)   │   │    (parse + bind)    │ │
│  └──────┬───────┘   └──────┬───────┘   └──────────┬───────────┘ │
│         │                  │                       │             │
│         ▼                  ▼                       ▼             │
│  ProjectInventory      DocInputSet          parsed + bound       │
│         │                  │               DocInputSet           │
│         └──────────────────┴───────────────────────┐            │
│                                                     ▼            │
│                                          ISymbolGraphBuilder     │
│                                          (Roslyn walk + merge)   │
├─────────────────────────────────────────────────────────────────┤
│                          CORE DOMAIN                            │
│                                                                  │
│         SymbolGraphSnapshot (versioned, deterministic)           │
│         SymbolNode / SymbolEdge / SymbolId / DocComment          │
│         SourceSpan / SnapshotRef / GraphDiff                     │
├─────────────────────────────────────────────────────────────────┤
│                         INDEXING LAYER                           │
│                                                                  │
│  ┌──────────────────────┐   ┌──────────────────────────────┐    │
│  │   ISearchIndex       │   │     IVectorIndex             │    │
│  │   (BM25 / keyword)   │   │   (embeddings, deferred V2)  │    │
│  └──────────┬───────────┘   └──────────────────────────────┘    │
│             │                                                    │
│             ▼                                                    │
│      artifacts/ snapshots + index files                          │
├─────────────────────────────────────────────────────────────────┤
│                          QUERY FACADE                            │
│                                                                  │
│              IKnowledgeQueryService                              │
│              (Search / GetSymbol / Diff)                         │
├─────────────────────────────────────────────────────────────────┤
│                         SERVING LAYER                            │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │               MCP Server (stdio transport)               │   │
│  │   search_symbols / get_symbol / get_references /         │   │
│  │   diff_snapshots / explain_project / review_changes      │   │
│  └──────────────────────────────────────────────────────────┘   │
│                  path allowlists / audit log / input validation  │
├─────────────────────────────────────────────────────────────────┤
│                       HOST / WIRING LAYER                        │
│                                                                  │
│   Aspire app host · DI registration · config · OpenTelemetry    │
└──────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| `IProjectSource` | Discover solution/project/XML doc files from a locator (path or URL) | Filesystem walker; future: git remote cache |
| `IDocSource` | Load raw XML documentation content given a `ProjectInventory` | Read `.xml` files output by `GenerateDocumentationFile` |
| `IXmlDocParser` | Parse XML doc content and bind entries to stable `SymbolId` values | `System.Xml` parse + `/// <member name="...">` ID string resolution |
| `ISymbolGraphBuilder` | Walk Roslyn symbols (`INamespaceSymbol`, `ITypeSymbol`, `IMethodSymbol`, etc.) and merge with parsed doc comments to produce a `SymbolGraphSnapshot` | `Microsoft.CodeAnalysis.CSharp` workspace + symbol visitor |
| `SymbolGraphSnapshot` | Immutable, versioned, deterministically-serializable graph of all symbols and their edges | Record type; written to `artifacts/` as JSON |
| `ISearchIndex` | Index a snapshot for keyword retrieval; answer `SearchAsync` + `GetAsync` queries | BM25 inverted index over `DisplayName` + `DocComment.Summary` fields |
| `IVectorIndex` | Upsert and retrieve by embedding similarity (deferred, interface only in V1) | Placeholder for future semantic search |
| `IKnowledgeQueryService` | Unified query facade: search, point-get by `SymbolId`, and diff two `SnapshotRef` values | Delegates to `ISearchIndex`; diff engine compares snapshots |
| MCP Tools | Narrow toolset mapping agent requests to `IKnowledgeQueryService` methods | `[McpTool]` handlers with path-allowlisted security wrapper |
| Roslyn Analyzers | Detect API changes not reflected in docs; detect suspicious edits | `DiagnosticAnalyzer` implementations registered to the build |
| Aspire Host | Wire DI, config, storage paths, telemetry | `IHostApplicationBuilder` + `AddDocAgentCore/Ingestion/McpServer` extensions |

## Recommended Project Structure

```
src/
├── DocAgent.Core/            # Pure domain — no IO, no framework deps
│   ├── Abstractions.cs       # All interfaces (IProjectSource, IDocSource, etc.)
│   └── Symbols.cs            # All domain types (SymbolNode, SymbolGraphSnapshot, etc.)
├── DocAgent.Ingestion/       # Ingestion pipeline implementation
│   ├── LocalProjectSource.cs # Filesystem project/solution discovery
│   ├── LocalDocSource.cs     # XML doc file loading
│   ├── XmlDocParser.cs       # Parse + bind doc comments to SymbolId
│   └── RoslynSymbolGraphBuilder.cs  # Roslyn walk + doc merge → SymbolGraphSnapshot
├── DocAgent.Indexing/        # Search index implementations
│   ├── InMemorySearchIndex.cs       # Stub; replace with BM25
│   ├── Bm25SearchIndex.cs           # BM25 inverted index (V1 target)
│   └── SnapshotStore.cs             # Read/write snapshots to artifacts/
├── DocAgent.Analysis/        # Diff engine + Roslyn analyzers
│   ├── SymbolDiffEngine.cs          # SnapshotRef → GraphDiff
│   └── Analyzers/                   # DiagnosticAnalyzer implementations
│       ├── ApiChangeWithoutDocAnalyzer.cs
│       └── SuspiciousEditAnalyzer.cs
├── DocAgent.McpServer/       # MCP server host
│   ├── Program.cs
│   ├── Tools/
│   │   └── DocTools.cs       # MCP tool handlers (search, get, diff, explain, review)
│   └── Security/
│       ├── PathAllowlist.cs
│       └── AuditLogger.cs
└── DocAgent.AppHost/         # Aspire app host + DI wiring
    └── Program.cs
```

### Structure Rationale

- **DocAgent.Core/:** Zero-dependency domain contracts. Everything else depends on this; it depends on nothing. This is the seam that enables testability and future polyglot extension.
- **DocAgent.Ingestion/:** All I/O-touching, source-format-specific code lives here. Isolating ingestion means the graph builder can be tested with injected data, and new source types (git remotes, NuGet) slot in without touching Core.
- **DocAgent.Indexing/:** Decoupled from ingestion — indexes consume `SymbolGraphSnapshot` values, not raw source material. This is the boundary that lets you swap BM25 for a vector index without touching the ingestion pipeline.
- **DocAgent.Analysis/:** Diff and analyzer logic separate from the serving layer. The diff engine operates on snapshots (pure data), making it testable in isolation.
- **DocAgent.McpServer/:** The serving boundary. Security enforcement lives here, not deeper. This layer should be thin: validate input, delegate to `IKnowledgeQueryService`, format output, log.

## Architectural Patterns

### Pattern 1: Compiler Pipeline Phases → Layered Components

**What:** Roslyn's own architecture exposes this: parse → declare → bind → emit. The equivalent here is discover → load → parse-and-bind → graph-build → index → serve. Each phase produces an immutable artifact consumed by the next.

**When to use:** Always — this is the foundational structure. Deviating from it (e.g., mixing parse and index logic) breaks testability and makes future polyglot extension impossible.

**Trade-offs:** Slightly more indirection than a monolithic pipeline, but each phase is independently testable with deterministic fixtures. The existing test stub follows this correctly.

**Example:**
```csharp
// Each stage is independently replaceable
var inventory = await projectSource.DiscoverAsync(locator, ct);
var docInputSet = await docSource.LoadAsync(inventory, ct);
var parsedDocs = xmlDocParser.Parse(docInputSet.XmlDocsByAssemblyName
    .Select(kv => (kv.Key, kv.Value)));
var snapshot = await graphBuilder.BuildAsync(inventory, parsedDocs, ct);
await searchIndex.IndexAsync(snapshot, ct);
```

### Pattern 2: Immutable Snapshot as the Interchange Format

**What:** The `SymbolGraphSnapshot` is the stable artifact between the ingestion/indexing boundary. It is versioned, deterministic (same input → same output), and serializable. This is the same principle LSIF uses: emit an immutable graph dump that can be indexed by any consumer without running the language server again.

**When to use:** Always for the V1 pipeline. The snapshot is written to `artifacts/` and reloaded on startup or on explicit refresh — the server does not hold a live Roslyn workspace in memory at query time.

**Trade-offs:** Snapshot must be re-built when source changes. Acceptable for V1 (build-time trigger). Live workspace queries are a V2+ concern.

**Example:**
```csharp
public sealed record SymbolGraphSnapshot(
    string SchemaVersion,        // bump on breaking schema changes
    string SourceFingerprint,    // hash of inputs — enables cache invalidation
    DateTimeOffset CreatedAt,
    IReadOnlyList<SymbolNode> Nodes,
    IReadOnlyList<SymbolEdge> Edges);
```

### Pattern 3: Narrow Tool Surface Over a Facade

**What:** MCP tools do not call Roslyn or the index directly. They call `IKnowledgeQueryService`, which is the single query facade. This is how LSP architectures work: the protocol handler is thin; the language server backend is the intelligence.

**When to use:** Always for the serving layer. Adding a new tool means adding a method to the facade and wiring a thin handler, not touching ingestion or indexing.

**Trade-offs:** One extra interface hop. Worth it — it allows the MCP server to be tested with a mock `IKnowledgeQueryService` without any Roslyn or filesystem dependency.

### Pattern 4: Interface-Scoped Capability Flags (Polyglot Extension)

**What:** Per `POLYGLOT_STRATEGY.md`, future language adapters declare capability flags (`HasSemanticTypes`, `HasCallGraph`, etc.) rather than implementing a fixed contract. The `Extensions` bag on `SymbolNode` holds language-specific fields. The core schema stays stable.

**When to use:** When adding Tier A (Tree-sitter) or Tier B (TS compiler, gopls) adapters in future milestones. Not needed in V1 — the current `ISymbolGraphBuilder` is non-generic; make it generic (`ISymbolGraphBuilder<TDoc>`) when the first polyglot adapter is added.

**Trade-offs:** Adds generics complexity. Deferred correctly to after V1 validation.

## Data Flow

### Ingestion Flow (Build-Time / On-Demand Refresh)

```
ProjectLocator (path or URL)
    │
    ▼
IProjectSource.DiscoverAsync()
    │
    ▼
ProjectInventory (solution files, project files, XML doc paths)
    │
    ├──────────────────────────────────────┐
    ▼                                      ▼
IDocSource.LoadAsync()           ISymbolGraphBuilder.BuildAsync()
    │                                      ▲
    ▼                                      │
DocInputSet (raw XML by assembly)          │
    │                                      │
    ▼                                      │
IXmlDocParser.Parse()                      │
    │                                      │
    ▼                                      │
DocInputSet (parsed + SymbolId-bound) ─────┘
                                           │
                                           ▼
                              SymbolGraphSnapshot
                              (versioned, fingerprinted)
                                           │
                              ┌────────────┴────────────┐
                              ▼                         ▼
                    ISearchIndex.IndexAsync()    artifacts/ (persist)
                              │
                              ▼
                      Index ready for queries
```

### Query Flow (Agent → MCP Tool → Facade → Index)

```
Agent sends MCP request
    │
    ▼
MCP Tool Handler
    │  (validate input, enforce path allowlist)
    ▼
IKnowledgeQueryService
    │
    ├── SearchAsync(query) ──────► ISearchIndex.SearchAsync()
    │                                    │
    │                                    ▼
    │                             BM25 hits → SearchHit[]
    │
    ├── GetSymbolAsync(id) ───────► ISearchIndex.GetAsync(id)
    │                                    │
    │                                    ▼
    │                             SymbolNode (with DocComment)
    │
    └── DiffAsync(a, b) ──────────► SymbolDiffEngine
                                         │
                                         ▼
                                    GraphDiff (structured findings)
    │
    ▼
MCP Tool formats response
    │  (serialize, audit log)
    ▼
Agent receives structured result
```

### Key Data Flows

1. **Snapshot invalidation:** `SourceFingerprint` (hash of input file mtimes or content hashes) determines whether a cached snapshot is still valid. On mismatch, ingestion re-runs and the index is rebuilt.
2. **Diff flow:** `DiffAsync(SnapshotRef a, SnapshotRef b)` loads both snapshots from the artifact store and compares `SymbolNode` sets by `SymbolId`. Signature, nullability, accessibility, and constraint changes are reported as structured `GraphDiff.Findings`.
3. **Doc binding:** `XmlDocParser` maps `/// <member name="M:Namespace.Type.Method(...)">` ID strings to `SymbolId` values that match what the Roslyn symbol walker emits. This is the critical join: without correct binding, doc comments detach from symbol nodes.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| Single repo, small solution | In-memory BM25 index, flat `artifacts/` directory — no external storage needed |
| Large solution (100k+ symbols) | Persist BM25 index to disk between runs; add SQLite for snapshot catalog and metadata queries |
| Multiple repos / enterprise | Content-addressed snapshot store (V2+); snapshot sharing across projects; optional vector index for semantic search |

### Scaling Priorities

1. **First bottleneck:** Roslyn workspace load time. A full solution with many projects takes seconds to load. Mitigation: cache the `SymbolGraphSnapshot` to `artifacts/` and only re-ingest when `SourceFingerprint` changes. This is the V1 design.
2. **Second bottleneck:** BM25 index size in memory for very large codebases. Mitigation: spill to disk (LuceneNet or a simple persistent inverted index). Deferred to V2.

## Anti-Patterns

### Anti-Pattern 1: Live Roslyn Workspace in the Query Path

**What people do:** Keep a `Compilation` or `Workspace` object alive in the MCP server and answer queries by calling Roslyn APIs at request time.

**Why it's wrong:** Roslyn workspace loading is slow (seconds for a real solution) and memory-intensive. It introduces non-determinism (source changes mid-session), makes the server harder to secure (it holds live filesystem handles), and makes testing expensive.

**Do this instead:** Build the `SymbolGraphSnapshot` at ingestion time and serve queries entirely from the in-memory index. The snapshot is the durable artifact — the Roslyn workspace is a build-time tool only.

### Anti-Pattern 2: Binding Doc Comments by Name String Matching

**What people do:** Match XML doc `<member name="...">` strings to symbol display names by substring or fuzzy match rather than by precise ID string parsing.

**Why it's wrong:** C# XML doc ID strings follow a precise format (`T:` for types, `M:` for methods with full parameter type encoding). Fuzzy matching produces silent mismatches — doc comments bind to the wrong symbol or get dropped. The resulting graph has corrupt edges.

**Do this instead:** Parse XML doc ID strings using the documented format (see `System.Xml.XPath` + the [XML documentation comments spec](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/)). Roslyn itself provides `DocumentationCommentId.GetFirstSymbolForDeclarationId()` for the reverse lookup.

### Anti-Pattern 3: Thin Core, Fat Tools

**What people do:** Put business logic (search ranking, diff logic, filtering) directly inside MCP tool handlers to ship faster.

**Why it's wrong:** Tool handlers are the security boundary and the protocol adapter. Logic in tool handlers cannot be tested without a running MCP server. It also couples business rules to the transport layer, making it impossible to call the same logic from a CLI, test harness, or alternative transport.

**Do this instead:** All logic lives in `IKnowledgeQueryService` and below. Tools are dumb: validate → call facade → format → return.

### Anti-Pattern 4: Mutable SymbolGraphSnapshot

**What people do:** Update the in-memory graph incrementally as files change, mutating nodes in place.

**Why it's wrong:** Mutable shared state in the serving path introduces race conditions, makes diffs unreliable (you can't compare a snapshot to itself after it changed), and destroys determinism. The snapshot fingerprint becomes meaningless.

**Do this instead:** Treat every snapshot as an immutable artifact. Incremental updates produce a new snapshot version. Old snapshots are retained for diff operations. This is exactly how LSIF is designed.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Roslyn (`Microsoft.CodeAnalysis.CSharp`) | Build-time only — create `AdhocWorkspace` or use `MSBuildWorkspace`, walk symbols, then discard | Never hold workspace open at query time |
| MCP SDK (`ModelContextProtocol`) | stdio transport, `[McpTool]` attribute-driven registration | Preview SDK; expect API churn; wrap in thin adapter layer |
| Aspire (`Microsoft.Extensions.Hosting`) | `IHostApplicationBuilder` extensions; config via `appsettings.json` | Standard .NET hosting; no Aspire-specific lock-in |
| OpenTelemetry | Structured logs via `Microsoft.Extensions.Logging`; trace tool calls with duration + status | Log every MCP tool invocation with requester identity |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| Core ↔ Ingestion | `IProjectSource`, `IDocSource`, `ISymbolGraphBuilder` interfaces | Ingestion depends on Core; Core has no knowledge of Ingestion |
| Ingestion ↔ Indexing | `SymbolGraphSnapshot` value (passed directly or via artifact store) | No interface needed — snapshot is the contract |
| Indexing ↔ Serving | `ISearchIndex`, `IKnowledgeQueryService` interfaces | Serving depends on Core interfaces only, not Indexing impl |
| Serving ↔ Host | DI registrations (`AddDocAgentCore()`, `AddDocAgentIngestion()`, `AddDocAgentMcpServer()`) | Host wires implementations to interfaces; serving layer is unaware of concrete types |
| Core ↔ Analysis | `SymbolGraphSnapshot`, `GraphDiff` — pure value types | Diff engine is a pure function over snapshots; no side effects |

## Suggested Build Order

The dependency graph dictates this order. Each phase is independently testable before the next phase is started.

```
Phase 1: Core Domain
    ↓ (all other components depend on this)
Phase 2: Ingestion
    2a. IProjectSource (LocalProjectSource) + IDocSource (LocalDocSource)
    2b. IXmlDocParser (proper XML parse + SymbolId binding)
    2c. ISymbolGraphBuilder (RoslynSymbolGraphBuilder — Roslyn walk + doc merge)
    ↓
Phase 3: Indexing
    3a. BM25 ISearchIndex (replace InMemorySearchIndex stub)
    3b. SnapshotStore (artifacts/ read/write)
    ↓
Phase 4: Query Facade
    IKnowledgeQueryService wired to ISearchIndex + SnapshotStore
    ↓
Phase 5: Serving (MCP Tools)
    5a. DocTools wired to IKnowledgeQueryService
    5b. Security layer (path allowlists, audit logging, input validation)
    ↓
Phase 6: Analysis
    6a. SymbolDiffEngine (diff_snapshots, review_changes)
    6b. Roslyn Analyzers (API change detection, suspicious edit detection)
    ↓
Phase 7: Host + Observability
    Aspire app host, DI wiring, telemetry
```

**Rationale:**
- Core must ship first — it is the dependency of everything else. It is already partially implemented.
- Ingestion before Indexing — the index consumes snapshots; you need a real snapshot to validate the index.
- Indexing before Query Facade — the facade delegates to the index; stub tests are insufficient for integration validation.
- Query Facade before MCP Tools — tools must be thin wrappers; business logic must be in the facade before tools are built.
- Analysis after Serving — diff and analyzers are additive features on top of a working pipeline, not prerequisites.
- Host last — wiring is straightforward once the components it wires exist.

## Sources

- [.NET Compiler Platform SDK concepts and object model (official Microsoft docs)](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/compiler-api-model) — HIGH confidence
- [LSIF Specification 0.4.0 — Language Server Index Format (Microsoft)](https://microsoft.github.io/language-server-protocol/specifications/lsif/0.4.0/specification/) — HIGH confidence
- [Language Server Protocol official site](https://microsoft.github.io/language-server-protocol/) — HIGH confidence
- [Roslyn GitHub — architecture overview](https://github.com/dotnet/roslyn/blob/main/docs/wiki/Roslyn-Overview.md) — HIGH confidence
- [Sourcegraph: code intelligence and LSP](https://sourcegraph.com/blog/sourcegraph-code-intelligence-and-the-language-server-protocol) — MEDIUM confidence
- Existing project contracts in `src/DocAgent.Core/Abstractions.cs` and `src/DocAgent.Core/Symbols.cs` — grounding source

---
*Architecture research for: .NET code documentation intelligence system with MCP server*
*Researched: 2026-02-26*
