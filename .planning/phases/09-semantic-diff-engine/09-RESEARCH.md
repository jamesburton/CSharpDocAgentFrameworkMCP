# Phase 9: Semantic Diff Engine - Research

**Researched:** 2026-02-28
**Domain:** C# domain modeling, immutable data comparison, semantic diffing
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Change categorization:**
- Each change has both a **type** (added, removed, modified) and a **severity** (breaking, non-breaking, informational)
- Breaking change detection scoped to **public API only** — changes to public/protected symbols. Internal/private changes are non-breaking by definition
- Changes stored as a **flat list** with parent symbol ID for context (not hierarchical tree)
- Change categories: signature, nullability, constraints, accessibility, dependency **plus doc comment changes**

**Diff output structure:**
- Include **summary statistics** — top-level counts by type and severity for quick triage
- **Complete result, consumer filters** — return full immutable diff, let MCP tools and consumers filter/slice as needed
- Each change entry carries **symbol IDs referencing both snapshots** for traceability
- **MessagePack serializable** — consistent with existing snapshot serialization

**Edge case handling:**
- **No rename detection** — treat as separate remove + add
- **No move detection** — treat as remove + add
- **Error on incompatible snapshots** — snapshots must be from the same project
- **Require complete snapshots** — no partial snapshot handling

**Change detail granularity:**
- Modified symbols include **both before/after values AND human-readable description**
- **Individual parameter diffs** — each parameter: added, removed, type changed, name changed, default value changed
- **Direct dependencies only** — what types/members a symbol directly references; transitive not included
- **Individual constraint diffs** — each generic constraint added/removed/changed tracked separately

### Claude's Discretion

- Internal data structures and algorithms for the diff engine
- Performance optimization approaches
- Exact MessagePack contract design for SymbolDiff
- How to match symbols across snapshots (by ID, by qualified name, etc.)

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| R-DIFF-ENGINE | Core diff types and algorithm for comparing two SymbolGraphSnapshots — detect signature, nullability, constraint, accessibility, dependency, and doc comment changes; produce deterministic immutable SymbolDiff result | Symbol matching strategy, change type taxonomy, immutable record patterns, MessagePack serialization, xUnit+FluentAssertions test patterns |
</phase_requirements>

## Summary

Phase 9 is a pure C# domain modeling and algorithm problem. There are no third-party libraries to add — this phase lives entirely in `DocAgent.Core` using the existing type system, with tests in `DocAgent.Tests`. The work is: define a rich `SymbolDiff` type hierarchy (sealed records, enums), implement a `SymbolGraphDiffer` class that walks two snapshots and produces deterministic results, and write xUnit tests covering every change category.

The codebase already has all the patterns needed: immutable `sealed record` types, `MessagePack`/`ContractlessStandardResolver` serialization, and xUnit+FluentAssertions tests. The existing `GraphDiff`/`DiffEntry` stub in `QueryTypes.cs` needs to be replaced or supplemented by the richer `SymbolDiff` types this phase creates.

The most important design decision — symbol matching strategy — should use `SymbolId` as the primary key (it's already the stable identifier used throughout). The `SymbolNode.PreviousIds` list exists specifically to handle renames, but per the locked decision, Phase 9 treats renames as remove+add (no rename detection), so matching by current `SymbolId` is sufficient and correct.

**Primary recommendation:** Model the diff as a sealed record hierarchy in `DocAgent.Core`, implement the differ as a stateless static class or simple service (no DI needed), and write one test class per change category using in-memory snapshot construction (following the `SnapshotSerializationTests` fixture pattern).

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| C# `sealed record` | .NET 10 / LangVersion=preview | Immutable domain types | Already used throughout Core — `SymbolNode`, `DocComment`, `SymbolEdge`, etc. |
| MessagePack `ContractlessStandardResolver` | 3.1.4 | Serialization | Already used for snapshots; contractless = no attributes needed on new types |
| xUnit | 2.9.3 | Tests | Project standard |
| FluentAssertions | 6.12.1 | Test assertions | Project standard — `.Should().Be()`, `.Should().HaveCount()`, etc. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.IO.Hashing` (XxHash64) | 9.0.0 | Determinism verification in tests | When verifying MessagePack roundtrip byte-identity |
| `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>` | BCL | Immutable collection contracts | All collection properties on diff types — consistent with `SymbolNode` pattern |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `sealed record` types | `class` with `IEquatable<T>` | Records give value equality and `with` expressions for free; no reason to deviate |
| Flat list of changes | Hierarchical tree | Locked decision — flat list with parent symbol ID for context |
| `ContractlessStandardResolver` | Attributed MessagePack | Contractless avoids adding `[MessagePackObject]` attributes to Core types; consistent with snapshot pattern |

**Installation:** No new packages needed. All dependencies already present.

## Architecture Patterns

### Recommended Project Structure

All new types go in `DocAgent.Core`. The differ implementation can go in `DocAgent.Core` or a new `DocAgent.Diffing` project — given the phase is "Core diff types AND algorithm", placing both in `DocAgent.Core` is simplest and consistent with the existing architecture.

```
src/DocAgent.Core/
├── Symbols.cs             # Existing — SymbolNode, SymbolEdge, SymbolGraphSnapshot
├── Abstractions.cs        # Existing — interfaces
├── QueryTypes.cs          # Existing — GraphDiff stub (may need updating/replacing)
├── DiffTypes.cs           # NEW — SymbolDiff, SymbolChange, change detail types
└── SymbolGraphDiffer.cs   # NEW — the diffing algorithm

tests/DocAgent.Tests/
├── SemanticDiff/
│   ├── SymbolGraphDifferTests.cs      # Core algorithm tests (added/removed/modified)
│   ├── SignatureChangeTests.cs        # Parameter + return type diffs
│   ├── NullabilityChangeTests.cs      # Nullable annotation diffs
│   ├── ConstraintChangeTests.cs       # Generic constraint diffs
│   ├── AccessibilityChangeTests.cs    # Accessibility modifier diffs
│   ├── DependencyChangeTests.cs       # Edge-based dependency diffs
│   ├── DocCommentChangeTests.cs       # Doc comment diffs
│   └── DiffDeterminismTests.cs        # Determinism + MessagePack roundtrip
```

### Pattern 1: Sealed Record Type Hierarchy for Change Details

**What:** Each category of change gets its own strongly-typed detail record. A discriminated union pattern is simulated with a base type and per-category subtypes.

**When to use:** When the planner needs to expose all change categories as first-class types that consumers (Phase 11 MCP tools) can pattern-match or filter on.

**Example (C# — aligned with existing Core patterns):**
```csharp
// Source: Existing DocAgent.Core/Symbols.cs and QueryTypes.cs patterns

public enum ChangeCategory
{
    Signature,      // parameter/return type changes
    Nullability,    // nullable annotation changes
    Constraint,     // generic constraint changes
    Accessibility,  // access modifier changes
    Dependency,     // edge-based dependency changes
    DocComment,     // documentation comment changes
}

public enum ChangeSeverity
{
    Breaking,       // public/protected symbol change visible to consumers
    NonBreaking,    // internal/private change, or additive public change
    Informational,  // doc comment, cosmetic
}

public enum ChangeType
{
    Added,
    Removed,
    Modified,
}

/// <summary>Base record for typed change details.</summary>
public abstract record ChangeDetail(string Description);

public sealed record SignatureChangeDetail(
    string Description,
    IReadOnlyList<ParameterChange> ParameterChanges,
    string? OldReturnType,
    string? NewReturnType) : ChangeDetail(Description);

public sealed record ParameterChange(
    ChangeType ChangeType,
    string? ParameterName,
    string? OldType,
    string? NewType,
    string? OldDefault,
    string? NewDefault);

public sealed record NullabilityChangeDetail(
    string Description,
    string? OldAnnotation,
    string? NewAnnotation) : ChangeDetail(Description);

public sealed record ConstraintChangeDetail(
    string Description,
    string TypeParameterName,
    IReadOnlyList<string> RemovedConstraints,
    IReadOnlyList<string> AddedConstraints) : ChangeDetail(Description);

public sealed record AccessibilityChangeDetail(
    string Description,
    Accessibility OldAccessibility,
    Accessibility NewAccessibility) : ChangeDetail(Description);

public sealed record DependencyChangeDetail(
    string Description,
    IReadOnlyList<SymbolEdge> RemovedEdges,
    IReadOnlyList<SymbolEdge> AddedEdges) : ChangeDetail(Description);

public sealed record DocCommentChangeDetail(
    string Description,
    DocComment? OldDocs,
    DocComment? NewDocs) : ChangeDetail(Description);

/// <summary>A single change entry in the diff.</summary>
public sealed record SymbolChange(
    SymbolId SymbolId,           // stable ID (present in whichever snapshot has it)
    SymbolId? BeforeSnapshotSymbolId,  // for traceability — ID in "before" snapshot (null if added)
    SymbolId? AfterSnapshotSymbolId,   // for traceability — ID in "after" snapshot (null if removed)
    SymbolId? ParentSymbolId,    // containing type/namespace for context
    ChangeType ChangeType,
    ChangeCategory Category,
    ChangeSeverity Severity,
    ChangeDetail Detail);

/// <summary>Summary statistics for quick triage.</summary>
public sealed record DiffSummary(
    int TotalChanges,
    int Added,
    int Removed,
    int Modified,
    int Breaking,
    int NonBreaking,
    int Informational);

/// <summary>The complete immutable result of diffing two snapshots.</summary>
public sealed record SymbolDiff(
    string BeforeSnapshotVersion,
    string AfterSnapshotVersion,
    string ProjectName,
    DiffSummary Summary,
    IReadOnlyList<SymbolChange> Changes);
```

### Pattern 2: Symbol Matching Strategy

**What:** Match symbols across snapshots by `SymbolId.Value` (string key lookup). Build a `Dictionary<string, SymbolNode>` from each snapshot for O(1) lookup.

**When to use:** This is the only correct approach — `SymbolId` is the stable identifier, and no rename detection is needed per the locked decision.

```csharp
// Source: DocAgent.Core/Symbols.cs — SymbolId is the canonical key

public static class SymbolGraphDiffer
{
    public static SymbolDiff Diff(SymbolGraphSnapshot before, SymbolGraphSnapshot after)
    {
        if (before.ProjectName != after.ProjectName)
            throw new ArgumentException(
                $"Cannot diff snapshots from different projects: '{before.ProjectName}' vs '{after.ProjectName}'");

        var beforeIndex = before.Nodes.ToDictionary(n => n.Id.Value);
        var afterIndex = after.Nodes.ToDictionary(n => n.Id.Value);
        var beforeEdges = BuildEdgeIndex(before.Edges);
        var afterEdges = BuildEdgeIndex(after.Edges);

        var changes = new List<SymbolChange>();

        // Added symbols
        foreach (var id in afterIndex.Keys.Except(beforeIndex.Keys))
            changes.Add(BuildAddedChange(afterIndex[id], after));

        // Removed symbols
        foreach (var id in beforeIndex.Keys.Except(afterIndex.Keys))
            changes.Add(BuildRemovedChange(beforeIndex[id], before));

        // Modified symbols
        foreach (var id in beforeIndex.Keys.Intersect(afterIndex.Keys))
            changes.AddRange(BuildModifiedChanges(beforeIndex[id], afterIndex[id],
                beforeEdges, afterEdges));

        // Sort for determinism
        var sorted = changes
            .OrderBy(c => c.SymbolId.Value, StringComparer.Ordinal)
            .ThenBy(c => c.Category)
            .ToList();

        return new SymbolDiff(
            BeforeSnapshotVersion: before.SourceFingerprint,
            AfterSnapshotVersion: after.SourceFingerprint,
            ProjectName: before.ProjectName,
            Summary: BuildSummary(sorted),
            Changes: sorted);
    }
}
```

### Pattern 3: Breaking Change Severity Detection

**What:** A change is breaking if the symbol is `Public` or `Protected` (or `ProtectedInternal`). Internal/private changes are `NonBreaking`. Doc comment changes are `Informational`.

```csharp
private static ChangeSeverity DetermineSeverity(SymbolNode node, ChangeCategory category)
{
    if (category == ChangeCategory.DocComment)
        return ChangeSeverity.Informational;

    return node.Accessibility switch
    {
        Accessibility.Public => ChangeSeverity.Breaking,
        Accessibility.Protected => ChangeSeverity.Breaking,
        Accessibility.ProtectedInternal => ChangeSeverity.Breaking,
        _ => ChangeSeverity.NonBreaking,
    };
}
```

### Pattern 4: Existing GraphDiff Stub

The existing `GraphDiff` and `DiffEntry` types in `QueryTypes.cs` are a thin stub used by `IKnowledgeQueryService.DiffAsync`. These can either:
- Be kept as-is and `SymbolDiff` added as the richer type
- Have `DiffAsync` updated to return `SymbolDiff` directly

**Recommendation (Claude's discretion):** Keep the stub `GraphDiff` for the existing interface but add `SymbolDiff` as the authoritative diff type. Phase 11 will update the MCP tools to use `SymbolDiff`. This avoids breaking `IKnowledgeQueryService` in Phase 9.

### Anti-Patterns to Avoid

- **Hierarchical tree output:** The locked decision is a flat list with parent symbol ID. Don't nest changes inside their parent symbols.
- **Non-deterministic ordering:** Changes must be sorted before building the result list. Tests must verify sorted order.
- **Mutable state in diff types:** All types must be `sealed record` or `readonly record struct` — no setters.
- **Attribute-based MessagePack:** Don't add `[MessagePackObject]` attributes. The project uses `ContractlessStandardResolver` which works without attributes.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Serialization | Custom binary serializer | MessagePack `ContractlessStandardResolver` | Already in project, byte-deterministic |
| Test snapshot comparison | Custom comparison code | FluentAssertions `.Should().BeEquivalentTo()` | Handles nested record comparison correctly |
| String diff for doc comments | Levenshtein / diff algorithm | Simple before/after value capture | Doc comment changes: store old and new `DocComment` objects, description says "changed". Phase 11 can render diff. |

**Key insight:** The diff algorithm itself is pure C# set operations (`Except`, `Intersect`) on `SymbolId` keys. No external library is needed or helpful here.

## Common Pitfalls

### Pitfall 1: Non-Deterministic Change Order
**What goes wrong:** `Dictionary<K,V>.Keys` enumeration order is not guaranteed. `Except`/`Intersect` on dictionary keys produces results in hash-table order, which varies across runs.
**Why it happens:** .NET dictionary key enumeration is based on internal bucket order, not insertion order.
**How to avoid:** Sort the final `changes` list before constructing `SymbolDiff`. Follow the same pattern as existing snapshot node/edge sorting in `RoslynSymbolGraphBuilder` (nodes sorted by `StringComparer.Ordinal`, edges sorted by `From.Value`, `To.Value`, `Kind`).
**Warning signs:** `DiffDeterminismTests` test that serializes the same diff twice and compares bytes will fail.

### Pitfall 2: MessagePack Fails on Abstract Record Base
**What goes wrong:** `ContractlessStandardResolver` cannot serialize `abstract record` types or polymorphic hierarchies without the union formatter.
**Why it happens:** Contractless resolver uses property names but can't reconstruct concrete subtypes from an abstract base field.
**How to avoid:** Either (a) make `ChangeDetail` a non-abstract `sealed record` with nullable fields for each category's specific data, or (b) store the category-specific detail as a `string Description` plus a `JsonElement`/`string ExtraJson` payload, or (c) use a union type approach. **Recommended:** Make `ChangeDetail` a single concrete record with typed optional fields per category — avoids polymorphism entirely.

Alternative: Store `ChangeDetail` as a JSON string in the record and deserialize on demand. This is simpler for MessagePack but sacrifices strong typing.

**Best approach (Claude's discretion):** Given the project's emphasis on strong typing, represent each change category as a separate `SymbolChange` subtype or use a wrapper that avoids the abstract base. One clean option: store the category-specific detail as individual nullable properties on `SymbolChange` itself, keyed by `ChangeCategory`.

### Pitfall 3: `SymbolNode` Has No Typed Signature Fields
**What goes wrong:** `SymbolNode` stores `DisplayName` (a string like `"public string Foo(int x)"`) but does not have structured fields for return type, parameters, or generic constraints. These aren't in the current Core types.
**Why it happens:** v1.0 focused on search and navigation, not semantic comparison.
**How to avoid:** Either (a) extend `SymbolNode` with structured fields (return type string, parameter list, constraint list), or (b) parse `DisplayName` strings to extract signature components. Option (a) is cleaner and more correct. **Recommendation:** Add structured signature fields to `SymbolNode` as part of Phase 9. This is an additive change (new optional properties, `null` for existing snapshots).
**Warning signs:** Diff engine can only report "DisplayName changed" without structured fields, making Phase 11's `explain_change` much weaker.

### Pitfall 4: Comparing Snapshots from Different Projects
**What goes wrong:** A caller diffs two snapshots that happen to share no symbols, producing a diff that looks like "everything was removed and everything was added" — which is technically correct but semantically useless.
**Why it happens:** No guard against incompatible snapshots.
**How to avoid:** Validate `before.ProjectName == after.ProjectName` at the start of `Diff()` and throw `ArgumentException` if they differ. This matches the locked decision ("Error on incompatible snapshots").

### Pitfall 5: Edge-Based Dependency Changes Miss Edge Semantics
**What goes wrong:** Comparing raw `SymbolEdge` lists gives spurious dependency changes when symbol IDs are stable but edge kinds change (e.g., `Calls` → `References`).
**Why it happens:** Edge equality uses `(From, To, Kind)` — all three matter.
**How to avoid:** Group edges by `(From, To)` pair when detecting dependency changes. Report kind changes as modifications, not remove+add.

## Code Examples

### Building In-Memory Fixtures for Tests (following existing pattern)

```csharp
// Source: tests/DocAgent.Tests/SnapshotSerializationTests.cs fixture pattern

private static SymbolGraphSnapshot BuildSnapshot(params SymbolNode[] nodes)
    => new SymbolGraphSnapshot(
        SchemaVersion: "1.0.0",
        ProjectName: "TestProject",
        SourceFingerprint: Guid.NewGuid().ToString("N"),
        ContentHash: null,
        CreatedAt: DateTimeOffset.UtcNow,
        Nodes: nodes.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToList(),
        Edges: []);

private static SymbolNode BuildPublicMethod(string id, string displayName)
    => new SymbolNode(
        Id: new SymbolId(id),
        Kind: SymbolKind.Method,
        DisplayName: displayName,
        FullyQualifiedName: id,
        PreviousIds: [],
        Accessibility: Accessibility.Public,
        Docs: null,
        Span: null);
```

### Test Structure for Each Change Category

```csharp
// xUnit + FluentAssertions pattern consistent with existing test files

public sealed class SignatureChangeTests
{
    [Fact]
    public void Diff_detects_return_type_change()
    {
        var before = BuildSnapshot(BuildPublicMethod("Ns.Foo", "public string Foo()"));
        var after  = BuildSnapshot(BuildPublicMethod("Ns.Foo", "public int Foo()"));

        var diff = SymbolGraphDiffer.Diff(before, after);

        diff.Changes.Should().ContainSingle(c =>
            c.SymbolId == new SymbolId("Ns.Foo") &&
            c.Category == ChangeCategory.Signature &&
            c.Severity == ChangeSeverity.Breaking);
    }
}
```

### MessagePack Roundtrip Determinism Test

```csharp
// Source: tests/DocAgent.Tests/SnapshotSerializationTests.cs and DeterminismTests.cs patterns

[Fact]
public void SymbolDiff_MessagePack_roundtrip_is_deterministic()
{
    var before = BuildBaselineSnapshot();
    var after  = BuildModifiedSnapshot();
    var diff = SymbolGraphDiffer.Diff(before, after);

    var options = ContractlessStandardResolver.Options;
    var bytes1 = MessagePackSerializer.Serialize(diff, options);
    var bytes2 = MessagePackSerializer.Serialize(diff, options);

    bytes1.SequenceEqual(bytes2).Should().BeTrue();

    var roundtripped = MessagePackSerializer.Deserialize<SymbolDiff>(bytes1, options);
    roundtripped.Should().BeEquivalentTo(diff);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `GraphDiff`/`DiffEntry` stub (v1.0) | Rich `SymbolDiff` with per-category typed changes (v1.1) | Phase 9 | Enables Phase 11 MCP tools to provide structured, actionable diff output |
| No structured signature fields on `SymbolNode` | Add `ReturnType?`, `Parameters`, `Constraints` fields to `SymbolNode` | Phase 9 | Required for meaningful signature diffing |

## Open Questions

1. **`SymbolNode` extension scope**
   - What we know: `SymbolNode` has `DisplayName` (unstructured string) but no typed signature fields
   - What's unclear: How far to extend it — minimal (return type string + parameter list as strings) vs structured (typed `ParameterInfo` records with name/type/default)
   - Recommendation: Add structured `ParameterInfo` records (name, type string, default value string, is-params, is-ref/out) and a `string? ReturnType` field to `SymbolNode`. The `RoslynSymbolGraphBuilder` already walks Roslyn symbols and has all this information available. This is additive — no breaking change to existing snapshots (new fields will be null/empty on deserialized v1.0 snapshots).

2. **`SymbolChange.BeforeSnapshotSymbolId` vs `AfterSnapshotSymbolId` identity**
   - What we know: The locked decision says each change carries "symbol IDs referencing both snapshots for traceability"
   - What's unclear: For added/removed symbols, one side is null. For modified symbols, the ID is the same in both snapshots (same `SymbolId.Value`). So both fields carry the same value for modifications.
   - Recommendation: Keep both fields for API completeness. For modifications, both reference the same `SymbolId`. For added symbols, only `AfterSnapshotSymbolId` is set. For removed, only `BeforeSnapshotSymbolId`. This gives Phase 11 consistent traceability without needing conditional logic.

3. **Abstract vs. concrete `ChangeDetail`**
   - What we know: MessagePack `ContractlessStandardResolver` has limitations with polymorphism (see Pitfall 2)
   - What's unclear: Whether a union approach (`[Union]` attribute) is acceptable given project preference against attributes on Core types
   - Recommendation: Use a single concrete `SymbolChangeDetail` record with per-category optional properties, or make `SymbolChange` hold category-specific detail via separate nullable properties for each category type. Eliminates the abstract/polymorphism problem entirely.

## Validation Architecture

`workflow.nyquist_validation` is not set in `.planning/config.json` — this section is skipped.

## Sources

### Primary (HIGH confidence)
- Direct code reading: `src/DocAgent.Core/Symbols.cs` — existing type system
- Direct code reading: `src/DocAgent.Core/QueryTypes.cs` — existing `GraphDiff` stub
- Direct code reading: `tests/DocAgent.Tests/SnapshotSerializationTests.cs` — MessagePack + FluentAssertions patterns
- Direct code reading: `tests/DocAgent.Tests/DeterminismTests.cs` — determinism test patterns
- Direct code reading: `Directory.Packages.props` — all dependency versions
- Direct code reading: `.planning/phases/09-semantic-diff-engine/09-CONTEXT.md` — locked decisions

### Secondary (MEDIUM confidence)
- .NET `ContractlessStandardResolver` behavior with abstract types — known limitation from MessagePack library behavior; no polymorphic union support without `[Union]` attribute

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already in project, versions confirmed
- Architecture: HIGH — patterns directly observed in existing codebase
- Pitfalls: HIGH (determinism, MessagePack abstract types) / MEDIUM (edge semantics) — based on code reading and known MessagePack behavior
- SymbolNode extension scope: MEDIUM — depends on what RoslynSymbolGraphBuilder currently captures (would need to read that file to confirm)

**Research date:** 2026-02-28
**Valid until:** 2026-04-28 (stable — all dependencies locked in project)
