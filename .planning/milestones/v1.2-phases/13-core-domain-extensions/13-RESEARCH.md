# Phase 13: Core Domain Extensions - Research

**Researched:** 2026-03-01
**Domain:** C# record type extension, MessagePack backward compatibility, domain model evolution
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

None — all implementation decisions are Claude's Discretion.

### Claude's Discretion

**ProjectEntry & Solution Modeling**
- DAG model (ProjectEdge collection vs adjacency list on ProjectEntry)
- ProjectEntry metadata richness (minimal vs rich with TFM, output type, NuGet refs)
- SolutionSnapshot shape (wrapper around per-project snapshots vs single merged graph)
- ProjectOrigin type on SymbolNode (simple string vs typed ProjectId reference)

**Stub Node Design**
- Stub information depth (type-level only vs type + public members)
- Stub flag mechanism (IsStub bool, NodeKind discriminator, or both)
- Stub creation strategy (eager for all referenced types vs lazy for edge targets only)
- BM25 index treatment of stubs (completely excluded vs included with lower weight)

**EdgeScope & Cross-Project Edges**
- EdgeScope enum values (binary IntraProject/CrossProject vs ternary with External)
- Additional edge metadata (scope only vs denormalized project names)
- Scope computation timing (stored at ingestion vs derived at query time)
- Kind and Scope are orthogonal — no combinatorial explosion of composite kinds
- Phase 13 adds types/fields only; detection/population logic belongs in Phase 14

**Backward Compatibility**
- Test compatibility strictness — guided by success criteria "pass without modification"
- MessagePack serialization approach (explicit Key indices vs follow existing convention)
- Nullability pattern (C# nullable references vs Option wrapper) — project has nullable enabled
- Whether to version-bump SymbolGraphSnapshot to v1.2

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| GRAPH-01 | `SolutionSnapshot` aggregate type holds per-project `SymbolGraphSnapshot`s with solution-level metadata | New top-level record in Symbols.cs; per-project list is flat (not merged graph per STATE.md decision) |
| GRAPH-02 | Cross-project `SymbolEdge`s link symbols across project boundaries via EdgeScope enum | Add `EdgeScope` enum + optional `Scope` property on `SymbolEdge`; ContractlessStandardResolver gives null default on old edges |
| GRAPH-03 | Project dependency DAG is first-class data in `SolutionSnapshot` (ProjectEdge collection) | `ProjectEntry` records + `ProjectEdge` collection on `SolutionSnapshot`; DAG traversal done at query time |
| GRAPH-04 | Stub/metadata nodes for NuGet package types (type name, namespace, member signatures; flagged `IsExternal`) | `IsStub` bool on `SymbolNode`; `NodeKind` discriminator enum for index filtering per GRAPH-05 |
| GRAPH-05 | Stub nodes are filtered at index time to prevent BM25 search pollution (NodeKind discriminator) | `NodeKind` enum (Real vs Stub) on `SymbolNode`; `ISearchIndex.IndexAsync` filters `NodeKind == Stub` |
| GRAPH-06 | New fields on existing types use nullable/default values for backward compatibility with v1.0/v1.1 snapshots | ContractlessStandardResolver already in use — new nullable/default fields get null/false/0 on old artifacts automatically |
</phase_requirements>

---

## Summary

Phase 13 is a pure **type-layer extension** to `DocAgent.Core`. No IO, no ingestion logic, no MCP tool changes — only new C# records, enums, and nullable fields on existing records. The codebase is already built on `MessagePack 3.1.4` with `ContractlessStandardResolver`, which provides backward compatibility automatically: new fields added to records default to `null`/`false`/`0`/empty-list when old MessagePack artifacts are deserialized. This eliminates the most common source of risk in domain type evolution.

The existing domain types (`SymbolNode`, `SymbolEdge`, `SymbolGraphSnapshot`) are all C# `sealed record` types with positional constructor parameters. Extending them requires adding new positional parameters with default values — the only pattern that preserves both positional and `with`-expression construction. All 220+ existing tests construct these types directly, so the default values must be chosen so that existing call sites compile without modification (i.e., no required positional parameters added).

The STATE.md captures one critical architectural decision already made: the flat single-graph model is preserved — `SolutionSnapshot` holds a list of per-project `SymbolGraphSnapshot` references, not a merged graph. This keeps determinism and per-project identity intact and is the correct approach for Phase 13.

**Primary recommendation:** Add new fields to existing records as optional positional parameters with sensible defaults; introduce `SolutionSnapshot`, `ProjectEntry`, `ProjectEdge`, `EdgeScope`, and `NodeKind` as new types in `Symbols.cs`; add `projectFilter` optional parameter to `IKnowledgeQueryService.SearchAsync`; all existing tests pass without modification because new parameters are optional and ContractlessStandardResolver handles missing fields on old artifacts.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MessagePack | 3.1.4 | Binary serialization for snapshot artifacts | Already the project serializer; `ContractlessStandardResolver` handles backward compat |
| xUnit | (via existing test project) | Unit test framework | Already in use across 220+ tests |
| FluentAssertions | (via existing test project) | Assertion library | Already in use; expressive equality checks |

### No New Dependencies Required

Phase 13 is type-only — no new NuGet packages needed. All existing infrastructure handles the new types automatically.

---

## Architecture Patterns

### Recommended File Layout

All new types belong in `src/DocAgent.Core/`:

```
src/DocAgent.Core/
├── Symbols.cs          # Extend SymbolNode, SymbolEdge, SymbolGraphSnapshot; add SolutionSnapshot, ProjectEntry, ProjectEdge, EdgeScope, NodeKind
├── Abstractions.cs     # Extend IKnowledgeQueryService.SearchAsync signature
└── (no new files needed)
```

Tests belong in `tests/DocAgent.Tests/`:
```
tests/DocAgent.Tests/
└── SnapshotSerializationTests.cs    # Extend with backward-compat roundtrip tests for new fields
```

A new test file for the new types may be added:
```
tests/DocAgent.Tests/
└── SolutionSnapshotTests.cs         # Tests for SolutionSnapshot, ProjectEntry, ProjectEdge, EdgeScope, NodeKind
```

### Pattern 1: Optional Positional Parameter Extension (C# Records)

**What:** Add new fields to existing `sealed record` types as positional parameters with defaults at the END of the parameter list.
**When to use:** When existing tests construct the record directly by position and must not require modification.

```csharp
// BEFORE (existing)
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<GenericConstraint> GenericConstraints);

// AFTER (Phase 13 extension)
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<GenericConstraint> GenericConstraints,
    // v1.2 extensions — default values ensure existing call sites compile unchanged
    string? ProjectOrigin = null,
    NodeKind NodeKind = NodeKind.Real);
```

**Why this works:** C# positional record parameters with defaults are optional at all call sites. Existing tests that call `new SymbolNode(Id: ..., Kind: ..., ...)` by name or by position (all 11 params) continue to compile without modification.

### Pattern 2: New Enum as Discriminator

**What:** `NodeKind` enum separates real symbols from stubs. Used by indexing layer to filter.
**When to use:** When a boolean flag (`IsStub`) alone is insufficient for future extension but binary distinction is needed today.

```csharp
public enum NodeKind
{
    Real,    // default (0) — existing nodes deserialize as Real
    Stub     // NuGet/external type stub
}

public enum EdgeScope
{
    IntraProject,    // default (0) — existing edges deserialize as IntraProject
    CrossProject,
    External         // edge to a stub node outside any solution project
}
```

**Critical:** Enum default value (0) must be the "old/existing" behavior. `Real = 0` and `IntraProject = 0` ensure existing deserialized artifacts get the correct default.

### Pattern 3: SolutionSnapshot as Aggregate Root

**What:** New top-level record that holds the list of per-project snapshots plus solution metadata. Does NOT merge nodes/edges — preserves per-project identity.

```csharp
public sealed record ProjectEntry(
    string Name,
    string Path,
    IReadOnlyList<string> DependsOn);   // project names this project references

public sealed record ProjectEdge(
    string From,    // project name
    string To);     // project name

public sealed record SolutionSnapshot(
    string? SolutionName,
    IReadOnlyList<ProjectEntry> Projects,
    IReadOnlyList<ProjectEdge> ProjectDependencies,
    IReadOnlyList<SymbolGraphSnapshot> ProjectSnapshots,
    DateTimeOffset CreatedAt);
```

### Pattern 4: Interface Signature Extension with Optional Parameter

**What:** Add `projectFilter` to `IKnowledgeQueryService.SearchAsync` as optional parameter with `null` default.
**When to use:** When existing callers must not require modification but new callers gain scoping ability.

```csharp
// BEFORE
Task<QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>> SearchAsync(
    string query,
    SymbolKind? kindFilter = null,
    int offset = 0,
    int limit = 20,
    string? snapshotVersion = null,
    CancellationToken ct = default);

// AFTER — existing call sites unaffected
Task<QueryResult<ResponseEnvelope<IReadOnlyList<SearchResultItem>>>> SearchAsync(
    string query,
    SymbolKind? kindFilter = null,
    int offset = 0,
    int limit = 20,
    string? snapshotVersion = null,
    string? projectFilter = null,          // Phase 13 addition
    CancellationToken ct = default);
```

**Note:** Implementations of this interface (`KnowledgeQueryService`) will need updating, but the existing test assertions on the interface call sites will not need modification if tests call by name or use the default.

### Anti-Patterns to Avoid

- **Adding required positional parameters to existing records:** Breaks every call site in the test suite. All new parameters MUST have defaults.
- **Merging all nodes into one flat SymbolGraphSnapshot:** Explicitly rejected in STATE.md — breaks per-project identity and determinism.
- **Making `NodeKind.Stub = 0` (default):** Old deserialized nodes would become stubs. Default MUST be `Real = 0`.
- **Making `EdgeScope.CrossProject = 0`:** Old deserialized edges would be cross-project. Default MUST be `IntraProject = 0`.
- **Polymorphic base classes:** DiffTypes.cs already documents this anti-pattern ("no abstract base — MessagePack safe"). Use discriminator enum + nullable detail fields instead.
- **Versioning via separate type names:** Don't create `SymbolNodeV2` — extend in place with optional fields.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Backward-compat deserialization | Custom migration code | ContractlessStandardResolver | Resolver handles missing fields as null/default automatically |
| Type equality for records | Custom IEquatable | C# record structural equality | Records provide value equality for free |
| DAG cycle detection | Custom graph traversal | Keep simple — validate at ingestion (Phase 14) | Phase 13 is types-only; validation belongs in Phase 14 |

**Key insight:** ContractlessStandardResolver's property-name-based matching means new nullable/default fields simply don't appear in old MessagePack bytes and deserialize as their CLR default. No migration layer needed.

---

## Common Pitfalls

### Pitfall 1: Enum Default Value Is Wrong

**What goes wrong:** Old MessagePack artifacts deserialize the new `NodeKind` or `EdgeScope` field as `0`. If `0` maps to the wrong value (e.g., `Stub` or `CrossProject`), all existing nodes/edges get the wrong classification.
**Why it happens:** MessagePack encodes absent/missing numeric fields as 0.
**How to avoid:** Always declare the "safe existing behavior" value as `= 0` in enum definitions. `NodeKind.Real = 0`, `EdgeScope.IntraProject = 0`.
**Warning signs:** Tests that check `node.NodeKind == NodeKind.Real` fail on old snapshots.

### Pitfall 2: Breaking IKnowledgeQueryService Implementations

**What goes wrong:** Adding a non-optional parameter to `SearchAsync` breaks `KnowledgeQueryService` (implementing class) at compile time.
**Why it happens:** Interface implementation must match the interface signature exactly.
**How to avoid:** Use optional parameter with `null` default. Update the implementing class in the same commit.
**Warning signs:** Build error `does not implement interface member`.

### Pitfall 3: Positional Parameter Order Breaks Existing Tests

**What goes wrong:** Inserting a new parameter in the middle of a positional record parameter list shifts all subsequent parameters. Existing tests that pass positional args get wrong values silently or fail to compile.
**Why it happens:** C# records use constructor parameter position for `new T(...)` calls.
**How to avoid:** ALWAYS append new parameters at the END of the parameter list with defaults.
**Warning signs:** Test failures where values appear in wrong properties.

### Pitfall 4: SolutionSnapshot Not MessagePack-Serializable

**What goes wrong:** `SolutionSnapshot` fails to serialize because it contains `IReadOnlyList<SymbolGraphSnapshot>` nesting.
**Why it happens:** ContractlessStandardResolver handles nesting but only for concrete types it can serialize. All members must be serializable.
**How to avoid:** Keep `SolutionSnapshot` member types consistent with what ContractlessStandardResolver already handles (records, enums, primitives, IReadOnlyList). Add a roundtrip test.
**Warning signs:** `MessagePackSerializationException` at runtime.

---

## Code Examples

### Backward-Compatible Record Extension

```csharp
// Source: Pattern verified against SnapshotSerializationTests.cs + MessagePack ContractlessStandardResolver behavior
// Existing 11-parameter call sites continue to compile without changes
public sealed record SymbolNode(
    SymbolId Id,
    SymbolKind Kind,
    string DisplayName,
    string? FullyQualifiedName,
    IReadOnlyList<SymbolId> PreviousIds,
    Accessibility Accessibility,
    DocComment? Docs,
    SourceSpan? Span,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<GenericConstraint> GenericConstraints,
    string? ProjectOrigin = null,
    NodeKind NodeKind = NodeKind.Real);
```

### EdgeScope Extension on SymbolEdge

```csharp
// Existing: SymbolEdge(SymbolId From, SymbolId To, SymbolEdgeKind Kind)
// New: adds optional Scope with IntraProject (0) as default
public sealed record SymbolEdge(
    SymbolId From,
    SymbolId To,
    SymbolEdgeKind Kind,
    EdgeScope Scope = EdgeScope.IntraProject);
```

### Backward-Compat Deserialization Test Pattern

```csharp
// Source: SnapshotSerializationTests.cs — extend this pattern
[Fact]
public void OldSnapshot_deserializes_with_NodeKind_Real_default()
{
    // Build a snapshot WITHOUT NodeKind (simulates v1.0/v1.1 artifact)
    var oldSnapshot = new SymbolGraphSnapshot(..., Nodes: [
        new SymbolNode(id, kind, name, fqn, prevIds, access, docs, span, ret, parms, constraints)
        // no NodeKind — uses default
    ], ...);

    // Serialize (produces bytes without NodeKind field in old format)
    var bytes = MessagePackSerializer.Serialize(oldSnapshot, ContractlessStandardResolver.Options);

    // Deserialize into new type — NodeKind should default to Real
    var result = MessagePackSerializer.Deserialize<SymbolGraphSnapshot>(bytes, ContractlessStandardResolver.Options);
    result.Nodes[0].NodeKind.Should().Be(NodeKind.Real);
}
```

---

## Validation Architecture

> `workflow.nyquist_validation` is not set in `.planning/config.json` (key absent). Treating as disabled — section included per observed project test infrastructure.

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit (version from central package management) |
| Config file | No explicit xunit.runner.json — uses defaults |
| Quick run command | `dotnet test --filter "FullyQualifiedName~SnapshotSerialization" -q` |
| Full suite command | `dotnet test -q` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| GRAPH-01 | `SolutionSnapshot` record round-trips via MessagePack | unit | `dotnet test --filter "FullyQualifiedName~SolutionSnapshot" -q` | No — Wave 0 |
| GRAPH-02 | `SymbolEdge.Scope` defaults to `IntraProject` on old artifacts | unit | `dotnet test --filter "FullyQualifiedName~SnapshotSerialization" -q` | Extend existing |
| GRAPH-03 | `ProjectEntry` and `ProjectEdge` are in `SolutionSnapshot` with correct DAG structure | unit | `dotnet test --filter "FullyQualifiedName~SolutionSnapshot" -q` | No — Wave 0 |
| GRAPH-04 | `SymbolNode.NodeKind == NodeKind.Stub` for stub nodes | unit | `dotnet test --filter "FullyQualifiedName~SolutionSnapshot" -q` | No — Wave 0 |
| GRAPH-05 | `NodeKind` enum exists and `Stub` value is non-zero (non-default) | compile/unit | `dotnet build` + unit test | Compile check only |
| GRAPH-06 | v1.0/v1.1 MessagePack bytes deserialize with `ProjectOrigin=null`, `NodeKind=Real`, `Scope=IntraProject` | unit | `dotnet test --filter "FullyQualifiedName~SnapshotSerialization" -q` | Extend existing |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "FullyQualifiedName~SnapshotSerialization" -q`
- **Per wave merge:** `dotnet test -q`
- **Phase gate:** All 220+ existing tests pass + new type tests pass before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/DocAgent.Tests/SolutionSnapshotTests.cs` — covers GRAPH-01, GRAPH-03, GRAPH-04
- [ ] Extend `tests/DocAgent.Tests/SnapshotSerializationTests.cs` — covers GRAPH-02, GRAPH-06 backward-compat roundtrip

*(No framework install gaps — xUnit and FluentAssertions already in place)*

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Abstract base types for polymorphism | Per-category nullable detail fields (see DiffTypes.cs comment) | v1.1 | MessagePack safe — no polymorphic resolver needed |
| Explicit MessagePack `[Key(N)]` attributes | ContractlessStandardResolver (property-name matching) | v1.0 | No attributes needed; backward compat via null defaults |

**Decisions already codified in STATE.md:**
- Single flat snapshot model preserved (no merged graph)
- ContractlessStandardResolver handles backward compat via null/false/empty-list defaults
- Stub nodes capped to direct PackageReference assemblies (not transitive) — Phase 14 concern

---

## Open Questions

1. **Should `SymbolGraphSnapshot.SchemaVersion` be bumped to "1.2.0"?**
   - What we know: Field exists and is a string; no code currently parses this value for behavior decisions
   - What's unclear: Whether downstream consumers (Phase 15/16 tools) need to branch on version
   - Recommendation: Bump to "1.2.0" only in new `SymbolGraphSnapshot` instances produced by Phase 14 ingestion; Phase 13 is types-only so leave the constant in SnapshotStore, not in Core types

2. **`ProjectOrigin` as string vs typed `ProjectId`?**
   - What we know: Downstream phases (14, 15) need to look up which snapshot a node came from; simple string (project name) matches existing `SymbolGraphSnapshot.ProjectName` field
   - Recommendation: Use `string? ProjectOrigin = null` — matches existing `ProjectName` pattern, zero friction for Phase 14 population

3. **Should `SolutionSnapshot` itself be MessagePack-serialized?**
   - What we know: Phase 13 adds the type; Phase 14 may save it to disk
   - What's unclear: Whether the SnapshotStore needs a parallel `SolutionSnapshotStore`
   - Recommendation: Make `SolutionSnapshot` serialization-ready (all fields ContractlessStandardResolver-compatible) but do not add persistence in Phase 13 — defer to Phase 14

---

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection: `src/DocAgent.Core/Symbols.cs` — existing type definitions
- Direct codebase inspection: `src/DocAgent.Core/Abstractions.cs` — existing interface definitions
- Direct codebase inspection: `src/DocAgent.Core/DiffTypes.cs` — documented MessagePack pattern ("no abstract base — MessagePack safe")
- Direct codebase inspection: `src/DocAgent.Ingestion/SnapshotStore.cs` — `ContractlessStandardResolver.Options` confirmed in use
- Direct codebase inspection: `tests/DocAgent.Tests/SnapshotSerializationTests.cs` — serialization test patterns
- `.planning/STATE.md` — architectural decisions (flat model, ContractlessStandardResolver, stub cap)

### Secondary (MEDIUM confidence)
- MessagePack-CSharp documentation: ContractlessStandardResolver maps by property name, missing properties default to CLR default value — consistent with observed codebase usage pattern

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — codebase directly inspected, no new dependencies needed
- Architecture: HIGH — existing patterns in DiffTypes.cs and SnapshotSerializationTests.cs are directly applicable
- Pitfalls: HIGH — all pitfalls derived from direct codebase evidence (enum default issue is a known MessagePack property)

**Research date:** 2026-03-01
**Valid until:** 2026-04-01 (stable domain — MessagePack 3.1.4, .NET 10, no fast-moving dependencies)
