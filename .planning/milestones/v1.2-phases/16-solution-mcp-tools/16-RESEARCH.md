# Phase 16: Solution MCP Tools - Research

**Researched:** 2026-03-02
**Domain:** C# MCP tool implementation — solution-level query surface over SolutionSnapshot
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**explain_solution Output**
- Summary table format: each project as a row with name, node count, edge count, doc coverage %
- Project dependency DAG represented as adjacency list: JSON object mapping project -> dependencies array (e.g., `{"Web": ["Core", "Data"]}`)
- Doc coverage as simple percentage: one number per project (% of public types/members with XML docs)
- Stub node info: total count only — single number for total external/stub nodes across solution

**diff_snapshots Scope**
- Per-project sections: group changes by project, each showing added/removed/modified symbols
- Dedicated cross-project edge changes section separate from per-project diffs
- Reuse existing v1.1 SemanticDiffEngine per-project, then aggregate results + add cross-project edge diff layer on top
- Two explicit snapshot version/hash parameters ('before' and 'after') — agent controls exactly what to compare

**Error & Edge Cases**
- Single-project solutions: same JSON structure with empty sections (empty dependency array, note it's single-project). Predictable for agents.
- Projects added/removed between versions: dedicated 'Projects Added' / 'Projects Removed' sections at top of diff, then per-project symbol diffs for surviving projects
- Solution tools require SolutionSnapshot from ingest_solution — clear boundary, no fallback to single-project snapshots
- PathAllowlist violation: opaque not-found denial ("Solution not found"), matching DocTools/ChangeTools security pattern

**Consistency with Existing Tools**
- Separate SolutionTools class alongside DocTools and ChangeTools — clean separation of concerns
- Underscore naming convention: `explain_solution`, `diff_snapshots` — matches existing `search_symbols`, `get_symbol`, `get_references`
- Response format matches DocTools pattern (content array with type/text structure)
- Solution path parameter accepts .sln file path only, consistent with ingest_solution

### Claude's Discretion
- Internal service layer structure (whether to create ISolutionQueryService or similar)
- Exact JSON field names and nesting within the content text
- How to compute doc coverage percentage (which symbol kinds count)
- DI registration and wiring details

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TOOLS-04 | `diff_snapshots` works at solution level (diff two SolutionSnapshots) | SolutionSnapshot holds ProjectSnapshots; SymbolGraphDiffer.Diff() runs per-project; cross-project edges identifiable via EdgeScope.CrossProject |
| TOOLS-05 | New `explain_solution` MCP tool provides solution-level architecture overview (project list, dependency DAG, node/edge counts, doc coverage per project) | SolutionSnapshot already has Projects (ProjectEntry[]), ProjectDependencies (ProjectEdge[]), ProjectSnapshots (SymbolGraphSnapshot[]); NodeKind.Stub identifies stub nodes; Docs field on SymbolNode enables coverage computation |
</phase_requirements>

---

## Summary

Phase 16 adds two MCP tools to `DocAgent.McpServer`: `explain_solution` (TOOLS-05) and an upgraded `diff_snapshots` (TOOLS-04) that spans an entire `SolutionSnapshot`. All data required by both tools already exists in-memory in `SolutionSnapshot` — which holds `ProjectEntry[]` (name, path, DependsOn), `ProjectEdge[]` (the project DAG), and `SymbolGraphSnapshot[]` (per-project symbol graphs with nodes and edges). No new ingestion or index work is required.

The key implementation challenge is that `SolutionSnapshot` is not directly persisted — only the flat merged `SymbolGraphSnapshot` (the `SnapshotId` returned from `ingest_solution`) is stored in the `SnapshotStore`. The caller to `diff_snapshots` for solution-level diffs must supply snapshot hashes that correspond to previously persisted flat snapshots, but the per-project breakdowns and the `SolutionSnapshot` aggregate reside only in memory during ingestion. This means **solution-level diff** must reconstruct per-project groupings from the flat merged snapshot using the `ProjectOrigin` field on `SymbolNode`, rather than loading a `SolutionSnapshot` from disk.

Security follows the established ChangeTools pattern: `PathAllowlist.IsAllowed(snapshotStore.ArtifactsDir)` guards entry; violations return opaque `"Solution not found"` errors. The new `SolutionTools` class is a `[McpServerToolType]`-annotated sealed class injected with `SnapshotStore`, `PathAllowlist`, `ILogger`, and `IOptions<DocAgentServerOptions>` — identical structure to `ChangeTools`.

**Primary recommendation:** Create `SolutionTools.cs` in `DocAgent.McpServer/Tools/`. Implement `explain_solution` by loading the latest flat snapshot from `SnapshotStore`, grouping nodes by `ProjectOrigin`, and computing per-project statistics inline. Implement `diff_snapshots` at solution scope by loading two flat snapshots, grouping by `ProjectOrigin`, running `SymbolGraphDiffer.Diff()` per-project pair, then separately extracting cross-project edge deltas.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `ModelContextProtocol.Server` | (already in project) | `[McpServerToolType]`, `[McpServerTool]`, `[Description]` attributes | Established pattern in DocTools and ChangeTools |
| `DocAgent.Core` | (project-internal) | `SolutionSnapshot`, `SymbolGraphSnapshot`, `SymbolGraphDiffer`, `PathAllowlist`, query types | All domain types live here |
| `System.Text.Json` | (.NET 10 built-in) | JSON serialization of MCP responses | Used throughout existing tools |
| `Microsoft.Extensions.Logging` | (.NET standard) | `ILogger<SolutionTools>` | Consistent with ChangeTools |
| `Microsoft.Extensions.Options` | (.NET standard) | `IOptions<DocAgentServerOptions>` for `VerboseErrors` | Consistent with ChangeTools |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `FluentAssertions` | (already in test project) | Assertion DSL in tests | All unit tests |
| `xUnit` | (already in test project) | Test runner | All unit tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Grouping by `ProjectOrigin` on flat snapshot | Loading `SolutionSnapshot` from disk | SolutionSnapshot not persisted; reconstructing from flat snapshot is the only option without architectural change |
| Inline doc-coverage computation | ISolutionQueryService abstraction | Abstraction adds complexity for a single-phase deliverable; Claude's discretion — justified only if multiple consumers emerge |

---

## Architecture Patterns

### Recommended Project Structure

```
src/DocAgent.McpServer/
└── Tools/
    ├── DocTools.cs          (existing — do NOT modify)
    ├── ChangeTools.cs       (existing — do NOT modify)
    ├── IngestionTools.cs    (existing — do NOT modify)
    └── SolutionTools.cs     (NEW — this phase)

tests/DocAgent.Tests/
└── SolutionToolTests.cs     (NEW — this phase)
```

### Pattern 1: SolutionTools Class Structure

**What:** Sealed `[McpServerToolType]` class with two `[McpServerTool]` methods, following the identical ChangeTools constructor pattern.

**When to use:** Consistent with all existing tool classes.

```csharp
[McpServerToolType]
public sealed class SolutionTools
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SnapshotStore _snapshotStore;
    private readonly PathAllowlist _allowlist;
    private readonly ILogger<SolutionTools> _logger;
    private readonly DocAgentServerOptions _options;

    public SolutionTools(
        SnapshotStore snapshotStore,
        PathAllowlist allowlist,
        ILogger<SolutionTools> logger,
        IOptions<DocAgentServerOptions> options)
    { ... }
}
```

### Pattern 2: PathAllowlist Gate

**What:** Check `_allowlist.IsAllowed(_snapshotStore.ArtifactsDir)` at the top of every tool method. Return opaque error on denial.

**When to use:** Every public tool method. Identical to ChangeTools pattern.

```csharp
if (!_allowlist.IsAllowed(_snapshotStore.ArtifactsDir))
{
    _logger.LogWarning("SolutionTools: snapshot store directory denied by allowlist");
    activity?.SetStatus(ActivityStatusCode.Error, "access_denied");
    return ErrorResponse(QueryErrorKind.NotFound, "Solution not found.");
}
```

Note: The opaque message must be `"Solution not found"` per the locked decision.

### Pattern 3: Load Flat Snapshot and Reconstruct Per-Project Groups

**What:** `SnapshotStore.LoadAsync(hash)` returns a `SymbolGraphSnapshot` (the merged flat snapshot). Group `Nodes` by `ProjectOrigin` to reconstruct per-project views.

**Key insight:** `SolutionSnapshot` (with the project DAG and per-project `SymbolGraphSnapshot[]`) is NOT persisted. Only the flat merged snapshot with `ProjectOrigin`-stamped nodes is stored. Solution-level tools must derive all per-project data from this flat snapshot.

```csharp
var snapshot = await _snapshotStore.LoadAsync(snapshotHash, ct);
if (snapshot is null)
    return ErrorResponse(QueryErrorKind.SnapshotMissing, "Solution not found.");

// Group by project
var nodesByProject = snapshot.Nodes
    .Where(n => n.NodeKind == NodeKind.Real) // exclude stubs
    .GroupBy(n => n.ProjectOrigin ?? "(unknown)")
    .ToDictionary(g => g.Key, g => g.ToList());

var edgesByProject = snapshot.Edges
    .Where(e => e.Scope == EdgeScope.IntraProject)
    .GroupBy(e => /* determine project from node lookup */ ...)
    .ToDictionary(g => g.Key, g => g.ToList());
```

### Pattern 4: explain_solution Implementation

**What:** Compute per-project summary from flat snapshot; build adjacency list DAG from `ProjectDependencies` stored in `SolutionSnapshot`. Since `SolutionSnapshot` is not in the flat store, the DAG must come from elsewhere OR the tool must explain the limitation.

**IMPORTANT DISCOVERY:** The `SolutionSnapshot` (including `ProjectDependencies` / `ProjectEdge[]`) is available as `SolutionIngestionResult.Snapshot` — but it is NOT stored in `SnapshotStore`. The flat `SymbolGraphSnapshot` stored by `SnapshotStore` does NOT contain the project DAG.

**Resolution options for the DAG (Claude's discretion):**
1. Derive the DAG from cross-project edges: for each `CrossProject` edge, note `FromProject → ToProject` pairs. This reconstructs the project dependency graph from edge data already in the flat snapshot.
2. Store `SolutionSnapshot` separately in a sidecar file alongside the flat snapshot in `SnapshotStore`. This requires `SnapshotStore` extension (low risk).
3. Omit the DAG from `explain_solution` if the flat snapshot was not from `ingest_solution`. Return empty `{}` for the dependency adjacency list.

**Recommended approach:** Derive DAG from cross-project edges (option 1). This requires no new storage and is derivable from existing data. The adjacency list is built by collecting unique `(fromProject, toProject)` pairs from edges with `EdgeScope.CrossProject`.

```csharp
// Build adjacency list from cross-project edges
var dagAdjacency = snapshot.Edges
    .Where(e => e.Scope == EdgeScope.CrossProject)
    .Select(e => (
        from: nodeById.TryGetValue(e.From.Value, out var fn) ? fn.ProjectOrigin ?? "?" : "?",
        to: nodeById.TryGetValue(e.To.Value, out var tn) ? tn.ProjectOrigin ?? "?" : "?"
    ))
    .Where(pair => pair.from != pair.to)
    .GroupBy(pair => pair.from)
    .ToDictionary(
        g => g.Key,
        g => (object)g.Select(p => p.to).Distinct().OrderBy(x => x).ToList()
    );
```

**Doc coverage computation (Claude's discretion):**
Doc coverage = count of public `SymbolNode`s with non-null `Docs?.Summary` / total public `SymbolNode`s, per project. Filter to `NodeKind.Real` only (exclude stubs). Which kinds count: suggest `Type`, `Method`, `Property`, `Constructor`, `Delegate`, `Event` — omit `Namespace`, `Parameter`, `TypeParameter`, `EnumMember`, `Destructor`, `Indexer`, `Operator`.

**Stub node count:** Global count of nodes where `NodeKind == NodeKind.Stub` across the entire flat snapshot.

### Pattern 5: diff_snapshots at Solution Scope

**What:** Load two flat snapshots (before/after), group each by `ProjectOrigin`, run `SymbolGraphDiffer.Diff()` per matching project pair, then separately diff cross-project edges.

**Key constraint from CONTEXT.md:** Projects added/removed between versions get dedicated sections at the top of the diff result. Only "surviving" projects (present in both snapshots) get per-project symbol diffs.

```csharp
// Reconstruct per-project snapshots from flat merged snapshot
// Each per-project SymbolGraphSnapshot must have:
//   ProjectName = projectName (for SymbolGraphDiffer.Diff() to work — it checks ProjectName equality)
//   Nodes = filtered subset
//   Edges = filtered subset (intra-project only)
//   SourceFingerprint = flat snapshot's SourceFingerprint (used as version label)

private static SymbolGraphSnapshot ExtractProjectSnapshot(
    SymbolGraphSnapshot flat,
    string projectName,
    IReadOnlyList<SymbolNode> nodes)
{
    var nodeIds = nodes.Select(n => n.Id.Value).ToHashSet(StringComparer.Ordinal);
    var edges = flat.Edges
        .Where(e => e.Scope == EdgeScope.IntraProject
                 && (nodeIds.Contains(e.From.Value) || nodeIds.Contains(e.To.Value)))
        .ToList();

    return flat with
    {
        ProjectName = projectName,
        Nodes = nodes.ToArray(),
        Edges = edges
    };
}
```

**Cross-project edge diff:**
```csharp
var beforeCrossEdges = snapshotA.Edges
    .Where(e => e.Scope == EdgeScope.CrossProject)
    .ToHashSet(new EdgeEqualityComparer());

var afterCrossEdges = snapshotB.Edges
    .Where(e => e.Scope == EdgeScope.CrossProject)
    .ToHashSet(new EdgeEqualityComparer());

var crossEdgesAdded = afterCrossEdges.Except(beforeCrossEdges).ToList();
var crossEdgesRemoved = beforeCrossEdges.Except(afterCrossEdges).ToList();
```

### Pattern 6: DI Registration

Add `SolutionTools` to DI in `ServiceCollectionExtensions.cs` or (if auto-discovered by MCP framework) simply mark with `[McpServerToolType]`. Check how DocTools/ChangeTools are registered — they appear to be auto-discovered via `[McpServerToolType]` attribute scanning in `Program.cs`.

**Verify registration pattern in Program.cs** before assuming auto-discovery. See `src/DocAgent.McpServer/Program.cs`.

### Anti-Patterns to Avoid

- **Trying to load SolutionSnapshot from SnapshotStore:** It is not stored there. Only the flat `SymbolGraphSnapshot` is. The `SolutionSnapshot` lives only in `SolutionIngestionResult.Snapshot` during the ingestion call.
- **Mutating SymbolGraphDiffer:** It is a static pure function; do not add solution-scope logic to it. Keep per-project diffs using it unchanged and aggregate above it.
- **Trusting ProjectOrigin == null as a valid project:** Nodes without `ProjectOrigin` come from pre-v1.2 single-project snapshots. The locked decision says: "Solution tools require SolutionSnapshot from ingest_solution." Return a clear error if all `ProjectOrigin` values are null (indicating a non-solution snapshot).
- **Returning verbose error messages on PathAllowlist violation:** The locked decision is opaque denial — `"Solution not found"`, not `"Access denied"` or anything that reveals the allowlist.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Per-project symbol diff | Custom diff logic | `SymbolGraphDiffer.Diff()` (existing) | Fully tested, handles all ChangeCategory/ChangeSeverity cases |
| MCP response serialization | Custom format | `System.Text.Json` with `JsonNamingPolicy.CamelCase` | Matches existing tools; agents expect camelCase |
| Path security | Custom path checks | `PathAllowlist.IsAllowed()` (existing) | Handles glob patterns, deny rules, env var overrides |

**Key insight:** All the hard parts (diff algorithm, security, serialization, MCP registration) are already solved. This phase is primarily composition and new output schema design.

---

## Common Pitfalls

### Pitfall 1: SolutionSnapshot Not Persisted

**What goes wrong:** Code attempts `_snapshotStore.LoadAsync<SolutionSnapshot>(...)` or similar, expecting the project DAG to be available from the artifact store. No such storage exists.

**Why it happens:** `SolutionIngestionResult.Snapshot` is populated in memory during ingestion but `SnapshotStore.SaveAsync()` only accepts `SymbolGraphSnapshot` (the flat merged graph).

**How to avoid:** Work entirely from the flat `SymbolGraphSnapshot` loaded via `SnapshotStore.LoadAsync()`. Reconstruct per-project views via `ProjectOrigin` grouping. Reconstruct the project DAG from `EdgeScope.CrossProject` edges.

**Warning signs:** Any reference to `SolutionIngestionResult` or `SolutionSnapshot` in `SolutionTools.cs` at runtime (outside of tests).

### Pitfall 2: SymbolGraphDiffer Rejects Mismatched ProjectName

**What goes wrong:** `SymbolGraphDiffer.Diff(before, after)` throws `ArgumentException` if `before.ProjectName != after.ProjectName`. When reconstructing per-project snapshots from two flat merged snapshots, the `ProjectName` fields of the reconstructed snapshots must match.

**Why it happens:** The differ uses `ProjectName` as a guard to prevent accidental cross-project diffs.

**How to avoid:** When calling `ExtractProjectSnapshot()`, always set `ProjectName = projectName` on both the before and after reconstructed snapshots, using the same string value.

**Warning signs:** `ArgumentException` during diff with message about "different projects."

### Pitfall 3: Stub Nodes Contaminating Per-Project Counts

**What goes wrong:** Stub nodes (`NodeKind.Stub`) appear in the flat snapshot alongside real nodes. Including them in per-project node counts inflates stats.

**Why it happens:** Stub nodes have `ProjectOrigin = null` by convention (they represent external types, not project-owned symbols).

**How to avoid:** Filter `NodeKind == NodeKind.Real` when computing per-project stats for `explain_solution`. Count stubs separately as the global total.

**Warning signs:** `explain_solution` returns a "(unknown)" project with high node count.

### Pitfall 4: Cross-Project Edge Project Attribution

**What goes wrong:** A `CrossProject` edge's `From` node belongs to project A and `To` node to project B, but naively grouping edges by `From` node's project creates an incomplete DAG (misses the reverse dependency direction).

**Why it happens:** Adjacency list needs unique `(projectA → projectB)` pairs. Multiple edges between the same project pair must be deduplicated.

**How to avoid:** Build a `Dictionary<SymbolId, string>` index of nodeId → projectName first. Then for each CrossProject edge, look up both endpoints, emit `(fromProject, toProject)` pair, deduplicate by pair before building adjacency list.

### Pitfall 5: doc_coverage Counting Wrong Symbol Kinds

**What goes wrong:** Counting every symbol kind (including `Namespace`, `Parameter`, `TypeParameter`) in the denominator inflates the total, making coverage appear lower than it is. Agents expect "public API members with docs."

**Why it happens:** `SymbolNode.Kind` includes many fine-grained kinds that traditionally don't carry standalone XML docs.

**How to avoid:** Restrict denominator to: `Type`, `Method`, `Property`, `Constructor`, `Delegate`, `Event`, `Field` — accessible with `Accessibility.Public` or `Accessibility.Protected` or `Accessibility.ProtectedInternal`.

---

## Code Examples

### explain_solution JSON response shape

```json
{
  "solutionName": "DocAgentFramework",
  "snapshotId": "abc123",
  "projects": [
    { "name": "DocAgent.Core", "nodeCount": 42, "edgeCount": 18, "docCoveragePercent": 87.5 },
    { "name": "DocAgent.Ingestion", "nodeCount": 31, "edgeCount": 12, "docCoveragePercent": 64.5 }
  ],
  "dependencyDag": {
    "DocAgent.Ingestion": ["DocAgent.Core"],
    "DocAgent.McpServer": ["DocAgent.Core", "DocAgent.Ingestion", "DocAgent.Indexing"]
  },
  "totalStubNodeCount": 15,
  "isSingleProject": false
}
```

### diff_snapshots JSON response shape (solution scope)

```json
{
  "before": "hash-before",
  "after": "hash-after",
  "projectsAdded": ["NewProject"],
  "projectsRemoved": [],
  "projectDiffs": {
    "DocAgent.Core": {
      "added": 2,
      "removed": 0,
      "modified": 3,
      "changes": [...]
    }
  },
  "crossProjectEdgeChanges": {
    "added": [{ "from": "DocAgent.Ingestion::SomeType", "to": "DocAgent.Core::OtherType", "kind": "References" }],
    "removed": []
  }
}
```

### Test pattern (consistent with ChangeToolTests)

```csharp
public sealed class SolutionToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SolutionToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SolutionToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(_tempDir);
    }

    public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

    private SolutionTools CreateTools(PathAllowlist? allowlist = null)
    {
        var opts = new DocAgentServerOptions { VerboseErrors = true };
        allowlist ??= new PathAllowlist(Options.Create(new DocAgentServerOptions { AllowedPaths = ["**"] }));
        return new SolutionTools(_store, allowlist, NullLogger<SolutionTools>.Instance, Options.Create(opts));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Single-project only diff_snapshots | Solution-level diff spanning all projects | Phase 16 (this phase) | diff_snapshots becomes multi-project aware |
| No solution overview tool | explain_solution provides structured DAG + coverage | Phase 16 (this phase) | Agents can orient themselves at solution level in one call |

---

## Open Questions

1. **Program.cs MCP tool registration — auto-discovery vs. explicit?**
   - What we know: `[McpServerToolType]` attribute exists on DocTools and ChangeTools
   - What's unclear: Whether `Program.cs` uses reflection-based auto-discovery or explicit registration per class
   - Recommendation: Read `src/DocAgent.McpServer/Program.cs` before writing the plan. If auto-discovery via `AddMcpServer().WithToolsFromAssembly()` pattern, no DI change needed. If explicit, add `SolutionTools` to the chain.

2. **SnapshotStore.ArtifactsDir property — does it exist?**
   - What we know: ChangeTools uses `_snapshotStore.ArtifactsDir` in the PathAllowlist guard
   - What's unclear: Whether this is a public property or if the field name is exactly `ArtifactsDir`
   - Recommendation: Read `SnapshotStore.cs` (src/DocAgent.Ingestion/SnapshotStore.cs) to confirm the property name before coding.

3. **isSingleProject flag — derivable from node ProjectOrigin?**
   - What we know: If all `Real` nodes have the same `ProjectOrigin`, it's effectively a single-project snapshot
   - What's unclear: Whether the `SymbolGraphSnapshot.SolutionName` field (nullable) reliably signals solution vs single-project ingestion
   - Recommendation: Check `SolutionName` nullability: if null, treat as single-project and note it in the response. If non-null, treat as solution-scope.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit + FluentAssertions |
| Config file | `tests/DocAgent.Tests/DocAgent.Tests.csproj` |
| Quick run command | `dotnet test --filter "FullyQualifiedName~SolutionTool"` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TOOLS-04 | `diff_snapshots` produces per-project sections for surviving projects | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-04 | `diff_snapshots` produces ProjectsAdded/ProjectsRemoved when project set changes | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-04 | `diff_snapshots` cross-project edge changes in dedicated section | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-04 | PathAllowlist denial returns opaque "Solution not found" | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-05 | `explain_solution` returns project list with node/edge counts | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-05 | `explain_solution` returns adjacency list DAG derived from cross-project edges | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-05 | `explain_solution` returns per-project doc coverage percentages | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-05 | `explain_solution` returns total stub node count | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |
| TOOLS-05 | `explain_solution` on single-project snapshot returns isSingleProject=true, empty DAG | unit | `dotnet test --filter "FullyQualifiedName~SolutionToolTests"` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~SolutionTool"`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/DocAgent.Tests/SolutionToolTests.cs` — covers all TOOLS-04 and TOOLS-05 behaviors
- [ ] Read `src/DocAgent.McpServer/Program.cs` to determine registration pattern before implementing

*(Framework install: none needed — xUnit and FluentAssertions already present)*

---

## Sources

### Primary (HIGH confidence)
- Codebase direct read — `src/DocAgent.McpServer/Tools/DocTools.cs` — full tool class pattern, constructor, FormatResponse, ErrorResponse helpers
- Codebase direct read — `src/DocAgent.McpServer/Tools/ChangeTools.cs` — PathAllowlist gate pattern, SnapshotStore usage, LoadAsync pattern
- Codebase direct read — `src/DocAgent.Core/SolutionTypes.cs` — SolutionSnapshot, ProjectEntry, ProjectEdge shapes
- Codebase direct read — `src/DocAgent.McpServer/Ingestion/SolutionIngestionResult.cs` — SolutionIngestionResult confirms `Snapshot` is nullable on the result, not persisted
- Codebase direct read — `src/DocAgent.Core/SymbolGraphDiffer.cs` — Diff() requires same ProjectName, pure static function
- Codebase direct read — `src/DocAgent.Core/Symbols.cs` — SymbolNode fields (ProjectOrigin, NodeKind), SymbolGraphSnapshot, EdgeScope values
- Codebase direct read — `src/DocAgent.McpServer/ServiceCollectionExtensions.cs` — current DI registrations (SolutionTools not yet registered)
- Codebase direct read — `tests/DocAgent.Tests/ChangeReview/ChangeToolTests.cs` — unit test pattern for tool classes
- Codebase direct read — `src/DocAgent.McpServer/Security/PathAllowlist.cs` — IsAllowed() semantics

### Secondary (MEDIUM confidence)
- `.planning/phases/16-solution-mcp-tools/16-CONTEXT.md` — user decisions (locked by stakeholder)
- `.planning/REQUIREMENTS.md` — requirement IDs TOOLS-04, TOOLS-05 confirmed as this phase's targets

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — directly verified from existing codebase
- Architecture: HIGH — pattern is direct extension of ChangeTools with same MCP framework
- Pitfalls: HIGH — derived from actual type constraints (SolutionSnapshot not persisted, SymbolGraphDiffer ProjectName check) verified from source
- Output shapes: HIGH — directly constrained by CONTEXT.md locked decisions

**Research date:** 2026-03-02
**Valid until:** 2026-04-02 (stable codebase, no fast-moving dependencies)
