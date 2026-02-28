# Phase 2: Ingestion Pipeline - Research

**Researched:** 2026-02-26
**Domain:** Roslyn symbol analysis, XML doc parsing, MSBuild workspace, deterministic serialization
**Confidence:** HIGH

## Summary

Phase 2 implements the core ingestion pipeline: discover .NET projects via MSBuild, walk Roslyn symbols to build a `SymbolGraphSnapshot`, parse and bind XML documentation comments, store versioned snapshots, and prove determinism. The Roslyn Compiler Platform provides all necessary APIs for symbol walking (`INamespaceSymbol.GetMembers()`, `INamedTypeSymbol.GetTypeMembers()`) and XML doc retrieval (`ISymbol.GetDocumentationCommentXml()`). However, `inheritdoc` expansion is NOT handled by Roslyn's public API and must be implemented manually.

A critical architectural decision: Roslyn 4.9+ runs MSBuild out-of-process via a build host, eliminating the need for `MSBuildLocator` and its `AssemblyLoadContext` isolation issues. The project currently pins Roslyn at 4.12.0 which includes this out-of-process architecture. The `Microsoft.CodeAnalysis.Workspaces.MSBuild` package (matching version 4.12.0) provides `MSBuildWorkspace` which handles solution/project loading.

For determinism, Roslyn's `GetMembers()` returns symbols in declaration order within a single file but does NOT guarantee a stable global ordering across partial types or multi-file compilations. The implementation must explicitly sort symbols by a deterministic key (e.g., documentation comment ID) to achieve byte-identical snapshots.

**Primary recommendation:** Use `Microsoft.CodeAnalysis.Workspaces.MSBuild` 4.12.0 (matching existing Roslyn pin) with out-of-process MSBuild. Sort all symbol collections by `GetDocumentationCommentId()` before building snapshot. Implement `inheritdoc` expansion manually by walking the base type chain.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Ingest **public and protected** symbols only (API surface + inheritance-visible members)
- **Full depth** nesting — walk all nested types recursively
- **All semantic edges**: containment, inheritance, interface implementation, parameter/return type references
- **Generated code included but tagged** — ingest symbols with `[GeneratedCode]` attribute or from obj/ but mark them with a flag so downstream consumers can filter
- **Synthesize placeholder** for symbols with no XML doc comment — generate minimal text like "No documentation provided" so every node has searchable text
- **Inherit docs from base types** when a derived member has no docs — copy base docs and mark as inherited
- **Best-effort parse for malformed XML** — extract what's parseable, attach a warning flag to the DocComment, don't fail the whole ingestion
- **Full structured parse** of XML doc elements: summary, remarks, param, returns, example, exception, seealso, typeparam into typed fields on DocComment
- **Solution-first with csproj override** — default to .sln file discovery for all projects; allow overriding with explicit .csproj paths for filtering
- **Exclude test projects by convention** — skip projects matching *.Tests, *.Test, *.Specs patterns; user can override to include
- **Pick highest TFM** for multi-targeted projects — use the newest target framework
- **Direct NuGet dependency public API ingestion** — ingest public symbols from direct NuGet package references
- **Content hash + git metadata** — content hash is the identity; git commit SHA and ingestion timestamp are metadata annotations
- **MessagePack storage in artifacts/** — store as `artifacts/{content-hash}.msgpack`
- **Manifest file** — `manifest.json` listing all snapshots with metadata

### Claude's Discretion
- Snapshot retention policy (keep all vs bounded)
- Exact manifest.json schema
- Roslyn workspace configuration details
- Compilation error handling strategy (warn vs skip vs fail)

### Deferred Ideas (OUT OF SCOPE)
- **Secondary source locations** — ability to list additional own-source directories beyond the solution
- **Git repo-linked dependency ingestion** — adding a git repo link for a NuGet package to ingest its source code directly
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INGS-01 | Roslyn symbol graph walker — namespaces, types, members with file spans and parent/child relationships | MSBuildWorkspace + Compilation API; `GlobalNamespace.GetMembers()` recursive walk; `ISymbol.Locations` for file spans; `ContainingType`/`ContainingNamespace` for parent-child |
| INGS-02 | XML doc parser with proper symbol binding (summary, param, returns, remarks, exceptions) | `ISymbol.GetDocumentationCommentXml()` returns raw XML per symbol; parse into `DocComment` record fields; bind via `GetDocumentationCommentId()` |
| INGS-03 | Handle XML doc edge cases: generics, partial types, overloads, operators, `inheritdoc` expansion | Generics use backtick notation in doc IDs; partial types merge across files; operators encode as `op_*`; `inheritdoc` requires manual resolution (not in Roslyn API) |
| INGS-04 | `SnapshotStore` — write/read versioned snapshots to `artifacts/snapshots/` | MessagePack serialization (already in Core); content hash via `System.IO.Hashing`; manifest.json for metadata |
| INGS-05 | Determinism test: same input produces byte-identical `SymbolGraphSnapshot` across runs | Explicit sorting of all symbol collections by doc comment ID before serialization; MessagePack with `ContractlessStandardResolver` is deterministic given stable input order |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.CodeAnalysis.CSharp.Workspaces | 4.12.0 | Roslyn C# workspace APIs for semantic analysis | Already pinned in project; provides Compilation, SemanticModel, symbol APIs |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 4.12.0 | MSBuildWorkspace for loading .sln/.csproj | Standard Roslyn approach; 4.12.0 uses out-of-process MSBuild (no MSBuildLocator needed) |
| MessagePack | 3.1.4 | Binary serialization for snapshots | Already in project; Phase 1 decision with ContractlessStandardResolver |
| System.IO.Hashing | 9.0.0 | Content hash computation | Already in project; XxHash128 for fast deterministic hashing |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.Json | (in-box) | manifest.json read/write | Manifest file serialization; no extra dependency |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| MSBuildWorkspace | Buildalyzer | More resilient to MSBuild quirks but adds dependency; MSBuildWorkspace 4.12+ with out-of-process build host resolves most historical issues |
| MSBuildLocator | (nothing) | Roslyn 4.9+ runs MSBuild out-of-process; MSBuildLocator is no longer needed |

**Installation (new packages for Ingestion project):**
```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" />
<PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
```
Add to `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.12.0" />
```

## Architecture Patterns

### Recommended Project Structure
```
src/DocAgent.Ingestion/
├── LocalProjectSource.cs       # IProjectSource: solution/csproj discovery
├── LocalDocSource.cs           # IDocSource: load XML doc files from build output
├── RoslynSymbolGraphBuilder.cs # ISymbolGraphBuilder: walk symbols, build snapshot
├── XmlDocParser.cs             # Parse XML doc comments into DocComment records
├── InheritDocResolver.cs       # Expand <inheritdoc/> by walking base type chain
├── SnapshotStore.cs            # Read/write snapshots + manifest.json
└── SymbolSorter.cs             # Deterministic ordering of symbol collections
```

### Pattern 1: Recursive Symbol Walker
**What:** Walk `Compilation.GlobalNamespace` recursively, collecting symbols into a flat list with parent-child edges.
**When to use:** Building the full symbol graph from a Roslyn Compilation.
**Example:**
```csharp
// Walk all accessible symbols recursively
void WalkNamespace(INamespaceSymbol ns, List<SymbolNode> nodes, List<SymbolEdge> edges)
{
    foreach (var member in ns.GetMembers().OrderBy(m => m.GetDocumentationCommentId()))
    {
        if (member is INamespaceSymbol childNs)
        {
            nodes.Add(ToSymbolNode(childNs));
            edges.Add(new SymbolEdge(ToId(ns), ToId(childNs), SymbolEdgeKind.Contains));
            WalkNamespace(childNs, nodes, edges);
        }
        else if (member is INamedTypeSymbol type)
        {
            if (!IsAccessible(type)) continue;
            nodes.Add(ToSymbolNode(type));
            edges.Add(new SymbolEdge(ToId(ns), ToId(type), SymbolEdgeKind.Contains));
            WalkType(type, nodes, edges);
        }
    }
}
```

### Pattern 2: MSBuildWorkspace Lifecycle
**What:** Open solution, get compilations, process, then dispose workspace to release memory.
**When to use:** Every ingestion run.
**Example:**
```csharp
// Roslyn 4.12+ — no MSBuildLocator needed
using var workspace = MSBuildWorkspace.Create();
workspace.SkipUnrecognizedProjects = true;
var solution = await workspace.OpenSolutionAsync(slnPath, ct);

foreach (var project in solution.Projects)
{
    if (IsTestProject(project.Name)) continue;
    var compilation = await project.GetCompilationAsync(ct);
    // Walk symbols from compilation
    // IMPORTANT: Do not hold references to Compilation after processing
}
// Workspace disposal releases all Roslyn memory
```

### Pattern 3: InheritDoc Resolution
**What:** Walk base type and interface chain to find documentation for undocumented members.
**When to use:** When `GetDocumentationCommentXml()` returns empty or contains `<inheritdoc/>`.
**Example:**
```csharp
DocComment? ResolveInheritDoc(ISymbol symbol, Compilation compilation)
{
    // 1. Check if symbol has <inheritdoc cref="..."/> — resolve explicit target
    // 2. For methods: walk overridden method chain (symbol.OverriddenMethod)
    // 3. For interface implementations: check interface member docs
    // 4. For types: walk BaseType chain
    // Mark resolved docs as inherited (add flag to DocComment)
    return resolvedDoc;
}
```

### Pattern 4: Deterministic Snapshot Construction
**What:** Sort all collections before serialization to guarantee byte-identical output.
**When to use:** Final step before MessagePack serialization.
**Example:**
```csharp
var sortedNodes = nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList();
var sortedEdges = edges
    .OrderBy(e => e.From.Value, StringComparer.Ordinal)
    .ThenBy(e => e.To.Value, StringComparer.Ordinal)
    .ThenBy(e => e.Kind)
    .ToList();

var snapshot = new SymbolGraphSnapshot(
    SchemaVersion: "1.0",
    ProjectName: projectName,
    SourceFingerprint: fingerprint,
    ContentHash: null, // Set by persistence layer
    CreatedAt: timestamp, // Must be fixed for determinism tests
    Nodes: sortedNodes,
    Edges: sortedEdges);
```

### Anti-Patterns to Avoid
- **Holding Compilation references after processing:** Roslyn Compilations hold the entire syntax tree and semantic model in memory. Process and release per-project.
- **Relying on GetMembers() ordering for determinism:** The order is declaration order per-file but undefined across partial classes or multi-file namespaces. Always sort explicitly.
- **Parsing XML docs as raw strings:** Use `System.Xml.Linq.XDocument` for structured parsing; string manipulation breaks on edge cases like nested elements and CDATA.
- **Calling GetDocumentationCommentXml() with expandIncludes=true in a loop:** Each call re-reads the include file. Batch XML doc loading instead.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MSBuild project loading | Custom .csproj parser | `MSBuildWorkspace.OpenSolutionAsync` | Handles project references, conditional imports, SDK-style projects, target framework resolution |
| XML doc comment ID generation | String formatting for doc IDs | `ISymbol.GetDocumentationCommentId()` | Handles all edge cases: generics (backtick notation), operators, explicit interface implementations, nested types |
| Symbol accessibility checking | Manual modifier parsing | `ISymbol.DeclaredAccessibility` | Roslyn resolves effective accessibility through nesting hierarchy |
| File span extraction | Syntax tree line counting | `ISymbol.Locations[0].GetLineSpan()` | Handles #line directives, mapped spans, partial declarations |
| Target framework resolution | TFM string parsing | `project.ParseOptions` / MSBuild properties | MSBuild already resolves the correct TFM based on build configuration |
| Binary serialization | Custom byte writers | MessagePack with `ContractlessStandardResolver` | Already proven in Phase 1; handles all record types automatically |

**Key insight:** Roslyn provides a complete semantic model. The ingestion layer should be a thin mapping from Roslyn's `ISymbol` hierarchy to the project's `SymbolNode`/`SymbolEdge` types, not a reimplementation of any compiler functionality.

## Common Pitfalls

### Pitfall 1: MSBuildWorkspace Silent Failures
**What goes wrong:** `MSBuildWorkspace.OpenSolutionAsync` succeeds but projects have zero documents or references.
**Why it happens:** MSBuild evaluation errors are swallowed into `workspace.Diagnostics` rather than thrown. Common causes: missing SDK, unsupported project type, conditional imports failing.
**How to avoid:** Always check `workspace.Diagnostics` after opening. Log warnings for any diagnostics. Verify each project has `Documents.Any()` before processing.
**Warning signs:** Compilation with zero syntax trees, no referenced assemblies.

### Pitfall 2: Non-Deterministic Symbol Ordering
**What goes wrong:** Same project produces different snapshot bytes on each run.
**Why it happens:** `GetMembers()` returns declaration order per file, but file enumeration order may vary. Partial classes across files produce symbols in file-discovery order. `Dictionary` iteration order is undefined.
**How to avoid:** Sort ALL collections by `StringComparer.Ordinal` on documentation comment ID. Use `SortedDictionary` or explicit `OrderBy` before snapshot construction. Fix `CreatedAt` timestamp in determinism tests.
**Warning signs:** Intermittent test failures comparing snapshot hashes.

### Pitfall 3: InheritDoc Infinite Loops
**What goes wrong:** Circular inheritance or self-referencing `inheritdoc cref` causes stack overflow.
**Why it happens:** Malformed code can have circular base type references. `inheritdoc cref` can point to the same symbol.
**How to avoid:** Track visited symbols in a `HashSet<SymbolId>` during resolution. Set a maximum depth (e.g., 10 levels). Return placeholder doc on cycle detection.
**Warning signs:** Stack overflow during doc resolution, infinite recursion warnings.

### Pitfall 4: Memory Pressure from Large Solutions
**What goes wrong:** Ingesting a large solution (50+ projects) causes OutOfMemoryException.
**Why it happens:** Holding all Compilation objects simultaneously. Each Compilation retains syntax trees, semantic models, and metadata references.
**How to avoid:** Process one project at a time. Dispose/null references to Compilation after extracting symbols. Call `GC.Collect()` between large projects if needed. Use `workspace.CloseSolution()` when done.
**Warning signs:** Memory usage climbing linearly with project count.

### Pitfall 5: Generated Code Detection False Positives
**What goes wrong:** User code incorrectly tagged as generated, or generated code missed.
**Why it happens:** Relying only on `[GeneratedCode]` attribute misses source-generator output. Relying only on file path (obj/) misses attribute-marked code.
**How to avoid:** Check BOTH: (1) `[GeneratedCode]` attribute via `ISymbol.GetAttributes()` and (2) source file path containing `/obj/` or `\obj\`. Tag but include both.
**Warning signs:** Missing symbols in snapshot, or excessive generated code noise.

### Pitfall 6: Malformed XML Doc Crashes
**What goes wrong:** `XDocument.Parse()` throws on malformed XML, aborting entire ingestion.
**Why it happens:** Real-world XML doc comments sometimes contain unescaped characters, unclosed tags, or invalid XML.
**How to avoid:** Wrap in try/catch. On parse failure, attempt recovery by wrapping in `<doc>` root, escaping common issues. If still failing, create DocComment with raw text in Summary and a warning flag.
**Warning signs:** `XmlException` during parsing.

## Code Examples

### Opening a Solution with MSBuildWorkspace (Roslyn 4.12+)
```csharp
// Source: Roslyn FAQ + NuGet docs
// No MSBuildLocator needed — Roslyn 4.9+ uses out-of-process build host
using var workspace = MSBuildWorkspace.Create();
workspace.SkipUnrecognizedProjects = true;

var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken);

// Check for load errors
foreach (var diag in workspace.Diagnostics)
{
    logger.LogWarning("MSBuild: {Message}", diag.Message);
}

foreach (var project in solution.Projects)
{
    var compilation = await project.GetCompilationAsync(cancellationToken);
    if (compilation is null) continue;

    // Check compilation diagnostics (errors vs warnings)
    var errors = compilation.GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error);
    // Decision: warn and continue (don't skip entire project for errors)
}
```

### Getting Documentation Comment XML
```csharp
// Source: Microsoft Learn — ISymbol.GetDocumentationCommentXml
string? xml = symbol.GetDocumentationCommentXml(
    preferredCulture: null,
    expandIncludes: true,
    cancellationToken: ct);

// Returns full XML like:
// <member name="M:Namespace.Class.Method(System.String)">
//   <summary>Description here.</summary>
//   <param name="arg">Parameter description.</param>
//   <returns>Return description.</returns>
// </member>
```

### Documentation Comment ID Format
```csharp
// Source: C# Language Specification — Documentation Comments
// Type: T:Namespace.ClassName
// Generic type: T:Namespace.ClassName`1
// Method: M:Namespace.ClassName.MethodName(System.String,System.Int32)
// Generic method: M:Namespace.ClassName.MethodName``1(``0)
// Property: P:Namespace.ClassName.PropertyName
// Operator: M:Namespace.ClassName.op_Addition(Namespace.ClassName,Namespace.ClassName)
// Conversion: M:Namespace.ClassName.op_Explicit(Namespace.ClassName)~System.Int32
// Indexer: P:Namespace.ClassName.Item(System.Int32)

string? docId = symbol.GetDocumentationCommentId();
```

### Extracting File Spans
```csharp
// Source: Roslyn API — ISymbol.Locations
var location = symbol.Locations.FirstOrDefault();
if (location?.IsInSource == true)
{
    var lineSpan = location.GetLineSpan();
    var span = new SourceSpan(
        FilePath: lineSpan.Path,
        StartLine: lineSpan.StartLinePosition.Line + 1,  // 0-based to 1-based
        StartColumn: lineSpan.StartLinePosition.Character + 1,
        EndLine: lineSpan.EndLinePosition.Line + 1,
        EndColumn: lineSpan.EndLinePosition.Character + 1);
}
```

### Checking Symbol Accessibility
```csharp
// Source: Roslyn API — ISymbol.DeclaredAccessibility
bool IsAccessible(ISymbol symbol) => symbol.DeclaredAccessibility switch
{
    Microsoft.CodeAnalysis.Accessibility.Public => true,
    Microsoft.CodeAnalysis.Accessibility.Protected => true,
    Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => true,
    Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => true,
    _ => false
};
```

### Detecting Generated Code
```csharp
// Source: Roslyn API — ISymbol.GetAttributes()
bool IsGeneratedCode(ISymbol symbol)
{
    // Check [GeneratedCode] attribute
    bool hasAttribute = symbol.GetAttributes()
        .Any(a => a.AttributeClass?.Name == "GeneratedCodeAttribute"
                && a.AttributeClass.ContainingNamespace.ToDisplayString()
                    == "System.CodeDom.Compiler");

    // Check file path for obj/ directory
    bool inObjDir = symbol.Locations
        .Any(loc => loc.IsInSource
            && (loc.SourceTree?.FilePath.Contains("/obj/") == true
             || loc.SourceTree?.FilePath.Contains("\\obj\\") == true));

    return hasAttribute || inObjDir;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| MSBuildLocator + in-process MSBuild | Out-of-process build host (Roslyn 4.9+) | Roslyn 4.9.0 (2024) | No need for MSBuildLocator; eliminates AssemblyLoadContext isolation issues |
| Manual XML doc ID construction | `ISymbol.GetDocumentationCommentId()` | Stable since Roslyn 1.x | Never hand-roll doc IDs |
| AppDomain isolation for MSBuild | Single process, out-of-proc build | .NET Core era | Simpler deployment, no ALC boundary management |

**Deprecated/outdated:**
- `MSBuildLocator.RegisterDefaults()` — Not needed with Roslyn 4.9+; the out-of-process build host handles MSBuild isolation
- `AppDomain.AssemblyResolve` for MSBuild assembly loading — .NET Core uses `AssemblyLoadContext` but with out-of-proc build this is moot

## Open Questions

1. **Build host availability on CI**
   - What we know: Roslyn 4.12's out-of-process build host requires .NET SDK installed on the machine. The build host DLL ships with the NuGet package.
   - What's unclear: Whether the build host works correctly in all CI environments (reported hanging issues on Windows Server 2022 with Roslyn 4.9+)
   - Recommendation: Test early in dev environment. If issues arise, consider Buildalyzer as fallback or pinning to a working Roslyn version.

2. **Roslyn version: stay at 4.12.0 or upgrade to 5.0.0?**
   - What we know: 5.0.0 released Nov 2025, targets .NET 8.0+. Project currently pins 4.12.0.
   - What's unclear: Whether 5.0.0 has breaking changes affecting this use case. STATE.md mentions a research flag to consider upgrading.
   - Recommendation: Stay at 4.12.0 for Phase 2 stability. Evaluate 5.0.0 upgrade as a separate task after pipeline is working.

3. **Compilation error handling strategy (Claude's discretion)**
   - What we know: Projects with compilation errors still produce partial symbol information.
   - Recommendation: **Warn and continue** — log compilation errors, proceed with available symbols. Skip only if Compilation is null (total failure). This maximizes ingestion coverage for real-world projects with transient errors.

4. **Snapshot retention policy (Claude's discretion)**
   - Recommendation: **Keep all** for V1. Disk is cheap; bounded retention adds complexity. Add a `prune` command later if needed.

5. **Manifest.json schema (Claude's discretion)**
   - Recommendation:
   ```json
   {
     "version": "1.0",
     "snapshots": [
       {
         "contentHash": "abc123...",
         "projectName": "MyProject",
         "gitCommitSha": "def456...",
         "ingestedAt": "2026-02-26T12:00:00Z",
         "schemaVersion": "1.0",
         "nodeCount": 150,
         "edgeCount": 300
       }
     ]
   }
   ```

## Sources

### Primary (HIGH confidence)
- Context7 `/dotnet/roslyn` — MSBuildWorkspace API, symbol walking patterns, GetMembers/GetNamespaceMembers
- [Microsoft Learn: ISymbol.GetDocumentationCommentXml](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.isymbol.getdocumentationcommentxml?view=roslyn-dotnet-4.9.0) — XML doc retrieval API
- [Microsoft Learn: Documentation Comments Specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments) — XML doc ID format
- [NuGet: Microsoft.CodeAnalysis.Workspaces.MSBuild 5.0.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/) — Package details and framework targets

### Secondary (MEDIUM confidence)
- [Dustin Campbell: Using MSBuildWorkspace](https://gist.github.com/DustinCampbell/32cd69d04ea1c08a16ae5c4cd21dd3a3) — Canonical MSBuildWorkspace usage guide
- [Roslyn Issue #372: Deterministic compilation](https://github.com/dotnet/roslyn/issues/372) — Determinism guarantees and limitations
- [Roslyn Discussion #50192: inheritdoc](https://github.com/dotnet/roslyn/discussions/50192) — Confirms inheritdoc not expanded by API
- [MSBuildLocator GitHub](https://github.com/microsoft/MSBuildLocator) — Confirmed not needed for Roslyn 4.9+

### Tertiary (LOW confidence)
- [Roslyn Issue #75967: OpenSolutionAsync hanging](https://github.com/dotnet/roslyn/issues/75967) — Potential CI issue with out-of-process build host (needs validation in our environment)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — Using established Roslyn APIs already partially in the project; MSBuildWorkspace is the canonical approach
- Architecture: HIGH — Symbol walking pattern is well-documented in Roslyn SDK samples and FAQ; mapping to existing domain types is straightforward
- Pitfalls: HIGH — Well-documented community experience with MSBuildWorkspace quirks, determinism issues, and memory management
- InheritDoc: MEDIUM — Manual implementation required; pattern is known from doc generators but not trivial

**Research date:** 2026-02-26
**Valid until:** 2026-03-28 (stable domain; Roslyn APIs change slowly)
